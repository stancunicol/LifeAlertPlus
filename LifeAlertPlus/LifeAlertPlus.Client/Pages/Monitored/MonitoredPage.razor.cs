using LifeAlertPlus.Client.Services;
using LifeAlertPlus.Shared.DTOs.Requests.Monitored;
using LifeAlertPlus.Shared.DTOs.Responses.ESP;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace LifeAlertPlus.Client.Pages.Monitored;

public partial class MonitoredPage : ComponentBase, IAsyncDisposable
{
    [Inject]
    private IJSRuntime JSRuntime { get; set; } = default!;

    [Inject]
    private MonitoredService MonitoredService { get; set; } = default!;

    [Inject]
    private UserMonitoredService UserMonitoredService { get; set; } = default!;

    [Inject]
    private UserService UserService { get; set; } = default!;

    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private TokenParserService TokenParser { get; set; } = default!;

    private string UserFullName = string.Empty;
    private string ProfilePictureUrl = string.Empty;
    private MonitorCreateRequestDTO newPerson = new();
    private string FilterStatus = "All";
    private bool ShowAddPersonModal;
    private string ErrorMessage = string.Empty;

    private Guid _currentUserId;
    private string? CurrentUserEmail;
    private IReadOnlyList<LifeAlertPlus.Domain.Entities.Monitored> _monitoredPeople = Array.Empty<LifeAlertPlus.Domain.Entities.Monitored>();
    private List<MonitoredCard> _monitoredCards = new();
    private bool _isLoadingMonitored = true;
    private string _dataError = string.Empty;
    private CancellationTokenSource? _pollingCts;
    private IEnumerable<MonitoredCard> AllPeople = new List<MonitoredCard>();
    private int CriticalCount => _monitoredCards.Count(c => GetCardStatus(c) == "Critical");
    private int WarningCount => _monitoredCards.Count(c => GetCardStatus(c) == "Warning");
    private int StableCount => _monitoredCards.Count(c => GetCardStatus(c) == "OK");

    protected override async Task OnInitializedAsync()
    {
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

            _monitoredPeople = await UserMonitoredService.GetMonitoredPeopleAsync(_currentUserId);
            //await RefreshEspDataAsync(_pollingCts?.Token ?? CancellationToken.None);
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
                await Task.Delay(TimeSpan.FromMinutes(2), token);
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
            try
            {
                latestData = await MonitoredService.GetEspDataAsync(person.DeviceSerialNumber, token);
            }
            catch (TaskCanceledException)
            {
                throw;
            }
            catch
            {
                // Ignore per-device failures to keep other updates flowing.
            }

            cards.Add(new MonitoredCard
            {
                Person = person,
                LastData = latestData,
                LastUpdatedUtc = DateTime.UtcNow
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
            "critical" => "Alert",
            "warning" => "Check needed",
            "ok" => "Stable",
            "nodata" => "No ESP data",
            _ => "Unknown"
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
             || string.IsNullOrEmpty(newPerson.Gender) || string.IsNullOrEmpty(newPerson.Relationship))
        {
            ErrorMessage = "All fields are required.";
            return;
        }

        var dto = new MonitorAddRequestDTO
        {
            MonitoredPerson = newPerson,
            CurrentUserEmail = CurrentUserEmail ?? string.Empty
        };

        var request = await MonitoredService.AddMonitoredPersonAsync(dto);

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
        return card.LastUpdatedUtc.ToLocalTime().ToString("g");
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
        const double temp = 36.8;
        return temp.ToString("F1");
    }

    private string FormatGpsStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "GPS: fără date";
        }

        var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Any(l => l.Contains(",V,", StringComparison.OrdinalIgnoreCase)))
        {
            return "GPS: fără fix (semnal slab)";
        }

        var gprmc = lines.FirstOrDefault(l => l.StartsWith("$GPRMC", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(gprmc))
        {
            var parts = gprmc.Split(',');
            if (parts.Length > 6 && !string.IsNullOrWhiteSpace(parts[3]) && !string.IsNullOrWhiteSpace(parts[5]))
            {
                var lat = parts[3] + parts.ElementAtOrDefault(4);
                var lon = parts[5] + parts.ElementAtOrDefault(6);
                return $"Coordonate: {lat} {lon}";
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
                return $"Coordonate: {lat} {lon}";
            }
        }

        return "GPS activ: date în curs";
    }

    private string FormatFallRisk(MonitoredCard card)
    {
        var mpu = card.LastData?.Mpu6050;
        var gyro = card.LastData?.Gyro;
        var hmc = card.LastData?.Hmc5883l;

        double accelScore = 0;
        if (mpu != null && mpu.Count >= 3)
        {
            accelScore = Math.Sqrt(mpu[0] * (double)mpu[0] + mpu[1] * (double)mpu[1] + mpu[2] * (double)mpu[2]);
        }

        double gyroScore = 0;
        if (gyro != null && gyro.Count >= 3)
        {
            gyroScore = Math.Sqrt(gyro[0] * (double)gyro[0] + gyro[1] * (double)gyro[1] + gyro[2] * (double)gyro[2]);
        }

        var highAccel = accelScore > 35000; // heuristic threshold
        var highGyro = gyroScore > 4000;    // heuristic threshold
        var hmcSpike = hmc.HasValue && Math.Abs(hmc.Value) > 500; // arbitrary spike detection

        if (highAccel || highGyro || hmcSpike)
        {
            return "Posibil eveniment (cădere/impact)";
        }

        return "Stabil";
    }

    private string GetCardStatus(MonitoredCard card)
    {
        if (card.LastData == null || !card.LastData.IsAvailable || card.LastData.Max30100 == null || card.LastData.Max30100.Count < 2)
        {
            return "NoData";
        }

        var pulse = card.LastData?.Max30100?.ElementAtOrDefault(0) ?? 0;
        var spo2 = card.LastData?.Max30100?.ElementAtOrDefault(1) ?? 0;
        var fallRisk = FormatFallRisk(card);

        if (fallRisk.Contains("Posibil eveniment") || pulse > 100 || pulse < 50 || spo2 < 90)
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

    public ValueTask DisposeAsync()
    {
        if (_pollingCts != null)
        {
            _pollingCts.Cancel();
            _pollingCts.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private sealed class MonitoredCard
    {
        public required LifeAlertPlus.Domain.Entities.Monitored Person { get; init; }
        public ESPDataResponseDTO? LastData { get; init; }
        public DateTime LastUpdatedUtc { get; init; }
    }
}