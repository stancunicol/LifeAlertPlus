using LifeAlertPlus.Client.Services;
using LifeAlertPlus.Shared.DTOs.Requests.Monitored;
using LifeAlertPlus.Shared.DTOs.Responses.ESP;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Globalization;

namespace LifeAlertPlus.Client.Pages.Monitored;

public partial class MonitoredPage : ComponentBase, IAsyncDisposable
{
    [Inject]
    private IJSRuntime JSRuntime { get; set; } = default!;

    [Inject]
    private MonitoredApiClient MonitoredApiClient { get; set; } = default!;

    [Inject]
    private UserMonitoredApiClient UserMonitoredApiClient { get; set; } = default!;

    [Inject]
    private UserApiClient UserApiClient { get; set; } = default!;

    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private TokenParserService TokenParser { get; set; } = default!;

    [Inject]
    private MeasurementApiClient MeasurementApiClient { get; set; } = default!;

    [Inject]
    private LanguageService Lang { get; set; } = default!;

    private string T(string key) => Lang.T(key);

    private int _userUpdateFrequency = 30;

    private string UserFullName = string.Empty;
    private string ProfilePictureUrl = string.Empty;
    private MonitorCreateRequestDTO newPerson = new();
    private string FilterStatus = "All";
    private string FilterOnlineStatus = "All";
    private string _searchQuery = string.Empty;
    private bool ShowAddPersonModal;
    private string ErrorMessage = string.Empty;

    // Archive view state
    private enum ViewMode { Active, Archive }
    private ViewMode CurrentView = ViewMode.Active;
    private bool _isLoadingArchive;
    private IReadOnlyList<LifeAlertPlus.Domain.Entities.Monitored> _archivedPeople = Array.Empty<LifeAlertPlus.Domain.Entities.Monitored>();
    private bool _showArchiveConfirm;
    private bool _showRestoreConfirm;
    private bool _showDeleteConfirm;
    private bool _showSoftDeleteConfirm;
    private LifeAlertPlus.Domain.Entities.Monitored? _personPendingAction;
    private string _actionError = string.Empty;
    private bool _isProcessingAction;

    private Guid _currentUserId;
    private string? CurrentUserEmail;
    private IReadOnlyList<LifeAlertPlus.Domain.Entities.Monitored> _monitoredPeople = Array.Empty<LifeAlertPlus.Domain.Entities.Monitored>();
    private List<MonitoredCard> _monitoredCards = new();
    private bool _isLoadingMonitored = true;
    private string _dataError = string.Empty;
    private CancellationTokenSource? _pollingCts;
    
    private IEnumerable<MonitoredCard> FilteredCards
    {
        get
        {
            var filtered = _monitoredCards.AsEnumerable();

            // Apply name search
            if (!string.IsNullOrWhiteSpace(_searchQuery))
            {
                var q = _searchQuery.Trim().ToLower();
                filtered = filtered.Where(c =>
                    $"{c.Person.FirstName} {c.Person.LastName}".ToLower().Contains(q));
            }

            // Apply status filter
            if (FilterStatus != "All")
            {
                filtered = filtered.Where(c => GetCardStatus(c) == FilterStatus);
            }

            // Apply online/offline filter
            if (FilterOnlineStatus == "Online")
            {
                filtered = filtered.Where(c => IsDataCurrent(c));
            }
            else if (FilterOnlineStatus == "Offline")
            {
                filtered = filtered.Where(c => !IsDataCurrent(c));
            }

            return filtered;
        }
    }
    
    private int CriticalCount => _monitoredCards.Count(c => GetCardStatus(c) == "Critical");
    private int WarningCount => _monitoredCards.Count(c => GetCardStatus(c) == "Warning");
    private int StableCount => _monitoredCards.Count(c => GetCardStatus(c) == "OK");
    private int OnlineCount => _monitoredCards.Count(c => IsDataCurrent(c));
    private int OfflineCount => _monitoredCards.Count(c => !IsDataCurrent(c));

    protected override async Task OnInitializedAsync()
    {
        Lang.OnLanguageChanged += HandleLanguageChanged;
        await LoadUserFromTokenAsync();

        if (_currentUserId == Guid.Empty)
        {
            _dataError = "User not authenticated.";
            _isLoadingMonitored = false;
            return;
        }

        // Load both lists in parallel so the archive tab count is correct from the start.
        await Task.WhenAll(LoadMonitoredPeopleAsync(), LoadArchivedPeopleAsync());
        StartPolling();
    }

    private async Task LoadUserFromTokenAsync()
    {
        var claims = await TokenParser.GetClaimsAsync();
        if (claims == null)
        {
            UserFullName = "User";
            CurrentUserEmail = string.Empty;
            _currentUserId = Guid.Empty;
            return;
        }

        CurrentUserEmail = claims.Email;
        _currentUserId = claims.UserId;
        UserFullName = $"{claims.FirstName} {claims.LastName}".Trim();
        ProfilePictureUrl = claims.ProfilePictureUrl;

        var userProfile = await UserApiClient.GetUserByIdAsync(claims.UserId);
        if (userProfile != null)
        {
            var apiName = $"{userProfile.FirstName} {userProfile.LastName}".Trim();
            if (!string.IsNullOrWhiteSpace(apiName))
                UserFullName = apiName;
            if (!string.IsNullOrWhiteSpace(userProfile.ProfilePictureUrl))
                ProfilePictureUrl = userProfile.ProfilePictureUrl;
            if (userProfile.UpdateFrequency > 0)
                _userUpdateFrequency = userProfile.UpdateFrequency;
        }
    }

    private async Task LoadMonitoredPeopleAsync()
    {
        _isLoadingMonitored = true;
        _dataError = string.Empty;

        try
        {
            if (_currentUserId == Guid.Empty)
            {
                _monitoredPeople = Array.Empty<LifeAlertPlus.Domain.Entities.Monitored>();
                _monitoredCards.Clear();
                return;
            }

            // Active list excludes archived people by default.
            _monitoredPeople = await UserMonitoredApiClient.GetMonitoredPeopleAsync(_currentUserId);
            await RefreshEspDataAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _dataError = $"Failed to load monitored people: {ex.Message}";
        }
        finally
        {
            _isLoadingMonitored = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task LoadArchivedPeopleAsync()
    {
        _isLoadingArchive = true;
        _dataError = string.Empty;

        try
        {
            if (_currentUserId == Guid.Empty)
            {
                _archivedPeople = Array.Empty<LifeAlertPlus.Domain.Entities.Monitored>();
                return;
            }

            _archivedPeople = await UserMonitoredApiClient.GetArchivedMonitoredPeopleAsync(_currentUserId);
        }
        catch (Exception ex)
        {
            _dataError = $"Failed to load archived people: {ex.Message}";
        }
        finally
        {
            _isLoadingArchive = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task SwitchViewAsync(ViewMode mode)
    {
        if (CurrentView == mode) return;
        CurrentView = mode;
        // Stop polling while in the archive view; archived data is static.
        if (mode == ViewMode.Archive)
        {
            _pollingCts?.Cancel();
            await LoadArchivedPeopleAsync();
        }
        else
        {
            await LoadMonitoredPeopleAsync();
            StartPolling();
        }
    }

    // ── Archive / Restore / Delete actions ──────────────────────────────────

    private void OpenArchiveConfirm(LifeAlertPlus.Domain.Entities.Monitored person)
    {
        _personPendingAction = person;
        _actionError = string.Empty;
        _showArchiveConfirm = true;
    }

    private void OpenRestoreConfirm(LifeAlertPlus.Domain.Entities.Monitored person)
    {
        _personPendingAction = person;
        _actionError = string.Empty;
        _showRestoreConfirm = true;
    }

    private void OpenDeleteConfirm(LifeAlertPlus.Domain.Entities.Monitored person)
    {
        _personPendingAction = person;
        _actionError = string.Empty;
        _showDeleteConfirm = true;
    }

    private void OpenSoftDeleteConfirm(LifeAlertPlus.Domain.Entities.Monitored person)
    {
        _personPendingAction = person;
        _actionError = string.Empty;
        _showSoftDeleteConfirm = true;
    }

    private void CloseActionModal()
    {
        _showArchiveConfirm = false;
        _showRestoreConfirm = false;
        _showDeleteConfirm = false;
        _showSoftDeleteConfirm = false;
        _personPendingAction = null;
        _actionError = string.Empty;
    }

    private async Task ConfirmArchiveAsync()
    {
        if (_personPendingAction == null) return;
        _isProcessingAction = true;
        _actionError = string.Empty;
        try
        {
            var ok = await MonitoredApiClient.ArchiveMonitoredPersonAsync(_personPendingAction.Id);
            if (!ok)
            {
                _actionError = T("archive.actionFailed");
                return;
            }
            CloseActionModal();
            await LoadMonitoredPeopleAsync();
        }
        finally
        {
            _isProcessingAction = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task ConfirmRestoreAsync()
    {
        if (_personPendingAction == null) return;
        _isProcessingAction = true;
        _actionError = string.Empty;
        try
        {
            var ok = await MonitoredApiClient.RestoreMonitoredPersonAsync(_personPendingAction.Id);
            if (!ok)
            {
                _actionError = T("archive.actionFailed");
                return;
            }
            CloseActionModal();
            await LoadArchivedPeopleAsync();
        }
        finally
        {
            _isProcessingAction = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task ConfirmDeleteAsync()
    {
        if (_personPendingAction == null) return;
        _isProcessingAction = true;
        _actionError = string.Empty;
        try
        {
            var ok = await MonitoredApiClient.DeleteMonitoredPersonAsync(_personPendingAction.Id);
            if (!ok)
            {
                _actionError = T("archive.actionFailed");
                return;
            }
            CloseActionModal();
            await LoadArchivedPeopleAsync();
        }
        finally
        {
            _isProcessingAction = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task ConfirmSoftDeleteAsync()
    {
        if (_personPendingAction == null) return;
        _isProcessingAction = true;
        _actionError = string.Empty;
        try
        {
            var result = await MonitoredApiClient.RemoveMonitoredAsync(_personPendingAction.Id);
            if (result == null)
            {
                _actionError = T("archive.actionFailed");
                return;
            }
            CloseActionModal();
            await LoadMonitoredPeopleAsync();
        }
        finally
        {
            _isProcessingAction = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private static string FormatArchivedDate(DateTime? archivedAt)
        => archivedAt.HasValue ? archivedAt.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm") : "—";

    private void StartPolling()
    {
        _pollingCts = new CancellationTokenSource();
        _ = PollLoopAsync(_pollingCts.Token);
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await RefreshEspDataAsync(token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Swallow unexpected errors so the fire-and-forget never crashes Blazor.
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RefreshEspDataAsync(CancellationToken token)
    {
        if (_monitoredPeople.Count == 0)
        {
            _monitoredCards.Clear();
            return;
        }

        var cardTasks = _monitoredPeople.Select(async person =>
        {
            token.ThrowIfCancellationRequested();

            var espTask = FetchEspAsync(person.DeviceSerialNumber, token);
            var measTask = FetchLastMeasurementTimeAsync(person.Id);
            await Task.WhenAll(espTask, measTask);

            return new MonitoredCard
            {
                Person = person,
                LastData = await espTask,
                LastUpdatedUtc = await measTask
            };
        });

        var cards = await Task.WhenAll(cardTasks);
        _monitoredCards = cards.ToList();
        await InvokeAsync(StateHasChanged);
    }

    private async Task<ESPDataResponseDTO?> FetchEspAsync(string serial, CancellationToken token)
    {
        try { return await MonitoredApiClient.GetEspDataAsync(serial, token); }
        catch (TaskCanceledException) { throw; }
        catch { return null; }
    }

    private async Task<DateTime> FetchLastMeasurementTimeAsync(Guid personId)
    {
        try
        {
            var ms = await MeasurementApiClient.GetMeasurementsByMonitoredIdAsync(personId, 1, 1);
            return ms?.FirstOrDefault()?.CreatedAt ?? DateTime.MinValue;
        }
        catch { return DateTime.MinValue; }
    }

    private string GetInitials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2)
        {
            return $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant();
        }

        return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpperInvariant();
    }

    private string GetStatusClass(string status)
    {
        return status.ToLower() switch
        {
            "critical" => "status-critical",
            "warning" => "status-warning",
            "ok" => "status-ok",
            "nodata" => "status-warning",
            _ => string.Empty
        };
    }

    private string GetStatusText(string status)
    {
        return status.ToLower() switch
        {
            "critical" => T("card.statusCritical"),
            "warning"  => T("card.statusCheckNeeded"),
            "ok"       => T("card.statusStable"),
            "nodata"   => T("card.statusNoEsp"),
            _          => T("card.noData")
        };
    }

    private void OpenAddPersonModal()
    {
        newPerson = new MonitorCreateRequestDTO
        {
            Birthdate = DateTime.Today
        };
        ShowAddPersonModal = true;
    }

    private void CloseAddPersonModal()
    {
        ShowAddPersonModal = false;
    }

    private async Task HandleAddPerson()
    {
        ErrorMessage = string.Empty;

        if (string.IsNullOrEmpty(newPerson.FirstName) || string.IsNullOrEmpty(newPerson.LastName) ||
            string.IsNullOrEmpty(newPerson.DeviceSerialNumber) || string.IsNullOrEmpty(newPerson.Address)
             || string.IsNullOrEmpty(newPerson.Gender))
        {
            ErrorMessage = "All fields are required.";
            return;
        }

        var dto = new MonitorAddRequestDTO
        {
            MonitoredPerson = newPerson,
            CurrentUserEmail = CurrentUserEmail ?? string.Empty
        };

        var request = await MonitoredApiClient.AddMonitoredPersonAsync(dto);

        if (!request)
        {
            ErrorMessage = "Failed to add monitored person. Please try again.";
            return;
        }

        await LoadMonitoredPeopleAsync();
        ShowAddPersonModal = false;
    }

    private string FormatIntList(IEnumerable<int>? values)
    {
        return values == null ? "N/A" : string.Join(", ", values);
    }

    private string FormatGps(string? values)
    {
        if (string.IsNullOrWhiteSpace(values))
        {
            return "N/A";
        }

        var lines = values
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        return lines.Length == 0 ? "N/A" : string.Join(" / ", lines);
    }

    private string FormatLastUpdate(MonitoredCard card)
    {
        if (card.LastUpdatedUtc == DateTime.MinValue)
            return "No data";
        
        return card.LastUpdatedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
    }

    private string FormatEspTimestamp(long value)
    {
        if (value > 1_000_000_000)
        {
            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(value).ToLocalTime().ToString("g");
            }
            catch
            {
                // fall through
            }
        }

        return $"T+{value}s";
    }

    private string GetPulse(ESPDataResponseDTO? data)
    {
        if (data?.Max30100 == null || data.Max30100.Count == 0)
        {
            return "N/A";
        }

        var pulse = data.Max30100[0];
        return pulse > 0 ? pulse.ToString() : "N/A";
    }

    private string GetOxygen(ESPDataResponseDTO? data)
    {
        if (data?.Max30100 == null || data.Max30100.Count < 2)
        {
            return "N/A";
        }

        var spo2 = data.Max30100[1];
        return spo2 > 0 ? spo2.ToString() : "N/A";
    }

    private string GetTemperature(ESPDataResponseDTO? data)
    {
        if (data?.Temperature != null)
        {
            return data.Temperature.Value.ToString("F1");
        }
        return "N/A";
    }

    private string FormatGpsStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return T("card.gpsIndoor");

        // NMEA "V" flag = no fix (indoors / weak signal)
        if (raw.Contains(",V,", StringComparison.OrdinalIgnoreCase))
            return T("card.gpsIndoor");

        // Plain "lat,lon" string
        var trimmed = raw.Trim();
        if (!trimmed.StartsWith("$") && trimmed.Contains(','))
        {
            var parts = trimmed.Split(',');
            if (parts.Length >= 2 &&
                double.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _) &&
                double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
                return T("card.gpsOutdoor");
        }

        // NMEA with valid fix
        if (raw.Contains("$GPRMC") || raw.Contains("$GPGLL"))
            return T("card.gpsOutdoor");

        return T("card.gpsIndoor");
    }

    // Use the firmware's own fall decision (3-state machine: freefall → impact → stillness).
    // The app must not re-implement fall detection from raw LSB data — thresholds differ.
    private static bool IsFallEvent(MonitoredCard card)
        => card.LastData?.IsFall == true;

    private string FormatFallRisk(MonitoredCard card)
    {
        if (!IsDataCurrent(card)) return T("card.statusOffline");
        return IsFallEvent(card) ? T("card.fallPossible") : T("card.fallStable");
    }

    private int GetEffectiveUpdateFrequency(LifeAlertPlus.Domain.Entities.Monitored person)
        => (person.UpdateFrequency ?? 0) > 0 ? person.UpdateFrequency!.Value : _userUpdateFrequency;

    private bool IsDataCurrent(MonitoredCard card)
    {
        var esp = card.LastData;
        if (esp == null || !esp.IsAvailable) return false;
        if (esp.Date <= 0) return true;
        var threshold = GetEffectiveUpdateFrequency(card.Person) + 15L;
        return (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - esp.Date) <= threshold;
    }

    private string GetCardStatus(MonitoredCard card)
    {
        if (!IsDataCurrent(card))
            return "NoData";

        var d     = card.LastData;
        var pulse = d?.Bpm  ?? (d?.Max30100?.Count >= 1 ? d.Max30100[0] : 0);
        var spo2  = d?.Spo2 ?? (d?.Max30100?.Count >= 2 ? d.Max30100[1] : 0);
        var temp  = card.LastData?.Temperature;

        int effectiveMinHr  = card.Person.MinHeartRate  ?? 60;
        int effectiveMaxHr  = card.Person.MaxHeartRate  ?? 100;
        double effectiveMinT = card.Person.MinTemperature ?? 36.0;
        double effectiveMaxT = card.Person.MaxTemperature ?? 37.5;
        int effectiveMinSpO2 = card.Person.MinSpO2 ?? 95;

        // Critical — same thresholds as SelectedMonitored (guards for 0 values)
        if (IsFallEvent(card)) return "Critical";
        if (pulse > 0 && (pulse > effectiveMaxHr || pulse < effectiveMinHr - 10)) return "Critical";
        if (spo2  > 0 && spo2 < 90) return "Critical";
        if (temp.HasValue && temp > 0 && (temp > effectiveMaxT + 0.5 || temp < effectiveMinT - 0.5)) return "Critical";

        // Warning
        if (pulse > 0 && (pulse > effectiveMaxHr - 10 || pulse < effectiveMinHr)) return "Warning";
        if (spo2  > 0 && spo2 < effectiveMinSpO2) return "Warning";
        if (temp.HasValue && temp > 0 && (temp > effectiveMaxT || temp < effectiveMinT)) return "Warning";

        return "OK";
    }

    private string GetCardStatusClass(MonitoredCard card)
    {
        return GetStatusClass(GetCardStatus(card));
    }

    private int GetAge(LifeAlertPlus.Domain.Entities.Monitored person)
    {
        if (person.Birthdate == null)
        {
            return 0;
        }

        var today = DateTime.Today;
        var age = today.Year - person.Birthdate.Value.Year;
        if (person.Birthdate.Value.Date > today.AddYears(-age))
        {
            age--;
        }

        return age;
    }

    private void ViewDetails(Guid personId)
    {
        NavigationManager.NavigateTo($"/monitored/{personId}");
    }

    protected async Task RequestLocation(Guid personId)
    {
        var card = _monitoredCards.FirstOrDefault(c => c.Person.Id == personId);
        var gps = card?.LastData?.Neo6m;

        if (string.IsNullOrWhiteSpace(gps) || !TryParseGpsToLatLon(gps, out var lat, out var lon))
        {
            // no coordinates available
            await JSRuntime.InvokeVoidAsync("alert", T("selected.noCoordinates"));
            return;
        }

        var url = $"https://www.google.com/maps/search/?api=1&query={lat.ToString(CultureInfo.InvariantCulture)},{lon.ToString(CultureInfo.InvariantCulture)}";
        await JSRuntime.InvokeVoidAsync("open", url, "_blank");
    }

    private bool TryParseGpsToLatLon(string gps, out double lat, out double lon)
    {
        lat = 0; lon = 0;
        if (string.IsNullOrWhiteSpace(gps)) return false;
        var s = gps.Trim();
        var parts = s.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;
        if (!double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out lat) &&
            !double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out lat))
            return false;
        if (!double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out lon) &&
            !double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out lon))
            return false;
        return true;
    }

    protected async Task CallPerson(Guid personId)
    {
        var card = _monitoredCards.FirstOrDefault(c => c.Person.Id == personId);
        var name = card?.Person != null ? $"{card.Person.FirstName} {card.Person.LastName}" : "Person";
        await JSRuntime.InvokeVoidAsync("alert", $"Call requested for {name}. Phone number not available.");
    }

    protected async Task TextPerson(Guid personId)
    {
        var card = _monitoredCards.FirstOrDefault(c => c.Person.Id == personId);
        var name = card?.Person != null ? $"{card.Person.FirstName} {card.Person.LastName}" : "Person";
        await JSRuntime.InvokeVoidAsync("alert", $"Text requested for {name}. Phone number not available.");
    }

    public ValueTask DisposeAsync()
    {
        Lang.OnLanguageChanged -= HandleLanguageChanged;
        if (_pollingCts != null)
        {
            _pollingCts.Cancel();
            _pollingCts.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private async void HandleLanguageChanged()
    {
        await InvokeAsync(StateHasChanged);
    }

    private sealed class MonitoredCard
    {
        public required LifeAlertPlus.Domain.Entities.Monitored Person { get; init; }
        public ESPDataResponseDTO? LastData { get; init; }
        public DateTime LastUpdatedUtc { get; init; }
    }
}