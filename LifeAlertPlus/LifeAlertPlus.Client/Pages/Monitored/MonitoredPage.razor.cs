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

    private string UserFullName = string.Empty;
    private string ProfilePictureUrl = string.Empty;
    private MonitorCreateRequestDTO newPerson = new();
    private string FilterStatus = "All";
    private string FilterOnlineStatus = "All";
    private bool ShowAddPersonModal;
    private string ErrorMessage = string.Empty;

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

            // Apply status filter
            if (FilterStatus != "All")
            {
                filtered = filtered.Where(c => GetCardStatus(c) == FilterStatus);
            }

            // Apply online/offline filter
            if (FilterOnlineStatus == "Online")
            {
                filtered = filtered.Where(c => c.LastData?.IsAvailable == true);
            }
            else if (FilterOnlineStatus == "Offline")
            {
                filtered = filtered.Where(c => c.LastData?.IsAvailable != true);
            }

            return filtered;
        }
    }
    
    private int CriticalCount => _monitoredCards.Count(c => GetCardStatus(c) == "Critical");
    private int WarningCount => _monitoredCards.Count(c => GetCardStatus(c) == "Warning");
    private int StableCount => _monitoredCards.Count(c => GetCardStatus(c) == "OK");
    private int OnlineCount => _monitoredCards.Count(c => c.LastData?.IsAvailable == true);
    private int OfflineCount => _monitoredCards.Count(c => c.LastData?.IsAvailable != true);

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

        await LoadMonitoredPeopleAsync();
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

        var cards = new List<MonitoredCard>();

        foreach (var person in _monitoredPeople)
        {
            token.ThrowIfCancellationRequested();

            ESPDataResponseDTO? latestData = null;
            DateTime lastMeasurementTime = DateTime.MinValue;
            
            try
            {
                latestData = await MonitoredApiClient.GetEspDataAsync(person.DeviceSerialNumber, token);
            }
            catch (TaskCanceledException)
            {
                throw;
            }
            catch
            {
                // Ignore per-device failures to keep other updates flowing.
            }

            // Get the last measurement for this person
            try
            {
                var measurements = await MeasurementApiClient.GetMeasurementsByMonitoredIdAsync(person.Id, 1, 1);
                var lastMeasurement = measurements?.FirstOrDefault();
                if (lastMeasurement != null)
                {
                    lastMeasurementTime = lastMeasurement.CreatedAt;
                }
            }
            catch
            {
                // If measurement fetch fails, continue with MinValue
            }

            cards.Add(new MonitoredCard
            {
                Person = person,
                LastData = latestData,
                LastUpdatedUtc = lastMeasurementTime
            });
        }

        _monitoredCards = cards;
        await InvokeAsync(StateHasChanged);
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
            "critical" => T("card.statusAlert"),
            "warning" => T("card.statusCheckNeeded"),
            "ok" => T("card.statusStable"),
            "nodata" => T("card.statusNoEsp"),
            _ => T("card.noData")
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
            return T("card.gpsNoData");

        var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Any(l => l.Contains(",V,", StringComparison.OrdinalIgnoreCase)))
            return T("card.gpsNoFix");

        var gprmc = lines.FirstOrDefault(l => l.StartsWith("$GPRMC", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(gprmc))
        {
            var parts = gprmc.Split(',');
            if (parts.Length > 6 && !string.IsNullOrWhiteSpace(parts[3]) && !string.IsNullOrWhiteSpace(parts[5]))
            {
                var lat = parts[3] + parts.ElementAtOrDefault(4);
                var lon = parts[5] + parts.ElementAtOrDefault(6);
                return $"{T("card.gpsCoordinates")}: {lat} {lon}";
            }
        }

        var gpgll = lines.FirstOrDefault(l => l.StartsWith("$GPGLL", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(gpgll))
        {
            var parts = gpgll.Split(',');
            if (parts.Length > 5 && !string.IsNullOrWhiteSpace(parts[1]) && !string.IsNullOrWhiteSpace(parts[3]))
            {
                var lat = parts[1] + parts.ElementAtOrDefault(2);
                var lon = parts[3] + parts.ElementAtOrDefault(4);
                return $"{T("card.gpsCoordinates")}: {lat} {lon}";
            }
        }

        return T("card.gpsActive");
    }

    private bool IsFallEvent(MonitoredCard card)
    {
        var mpu = card.LastData?.Mpu6050;
        var gyro = card.LastData?.Gyro;

        double accelScore = 0;
        if (mpu != null && mpu.Count >= 3)
            accelScore = Math.Sqrt(mpu[0] * (double)mpu[0] + mpu[1] * (double)mpu[1] + mpu[2] * (double)mpu[2]);

        double gyroScore = 0;
        if (gyro != null && gyro.Count >= 3)
            gyroScore = Math.Sqrt(gyro[0] * (double)gyro[0] + gyro[1] * (double)gyro[1] + gyro[2] * (double)gyro[2]);

        return accelScore > 35000 || gyroScore > 4000;
    }

    private string FormatFallRisk(MonitoredCard card)
        => IsFallEvent(card) ? T("card.fallPossible") : T("card.fallStable");

    private string GetCardStatus(MonitoredCard card)
    {
        if (card.LastData == null || !card.LastData.IsAvailable || card.LastData.Max30100 == null || card.LastData.Max30100.Count < 2)
        {
            return "NoData";
        }

        var pulse = card.LastData?.Max30100?.ElementAtOrDefault(0) ?? 0;
        var spo2 = card.LastData?.Max30100?.ElementAtOrDefault(1) ?? 0;

        if (IsFallEvent(card) || pulse > 100 || pulse < 50 || spo2 < 90)
        {
            return "Critical";
        }

        if (pulse > 90 || pulse < 60 || spo2 < 95)
        {
            return "Warning";
        }

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