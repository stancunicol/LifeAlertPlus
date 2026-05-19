using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using LifeAlertPlus.Client.Services;
using System.Globalization;

namespace LifeAlertPlus.Client.Pages.Dashboard;

public partial class DashboardPage : ComponentBase, IAsyncDisposable
{
    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private IJSRuntime JSRuntime { get; set; } = default!;

    [Inject]
    private TokenParserService TokenParser { get; set; } = default!;

    [Inject]
    private UserApiClient UserApiClient { get; set; } = default!;

    [Inject]
    private AuthApiClient AuthApiClient { get; set; } = default!;

    [Inject]
    private UserMonitoredApiClient UserMonitoredApiClient { get; set; } = default!;

    [Inject]
    private MonitoredApiClient MonitoredApiClient { get; set; } = default!;

    [Inject]
    private MeasurementApiClient MeasurementApiClient { get; set; } = default!;

    [Inject]
    private LanguageService Lang { get; set; } = default!;

    private string T(string key) => Lang.T(key);

    protected string UserFullName { get; set; } = "";
    protected string ProfilePictureUrl { get; set; } = "";
    // Start with zeros to avoid showing placeholder/demo values that then reset.
    protected int TotalMonitored { get; set; } = 0;
    protected int ActiveAlerts { get; set; } = 0;
    protected int StableCount { get; set; } = 0;
    protected int TodayMeasurements { get; set; } = 0;
    protected int OfflineCount { get; set; } = 0;

    protected IReadOnlyList<MonitoredSample> MonitoredSamples { get; private set; } = Array.Empty<MonitoredSample>();
    private Guid _currentUserId;
    private CancellationTokenSource? _pollingCts;

    protected override async Task OnInitializedAsync()
    {
        // TokenParserService.GetClaimsAsync() already handles reading the token from the
        // URL fragment (Google OAuth redirect), storing it in sessionStorage and cleaning the URL.
        // Doing it here first and returning early caused OnInitializedAsync to never run
        // a second time (same component instance), so user data was never loaded.
        var claims = await TokenParser.GetClaimsAsync();
        if (claims == null)
        {
            Navigation.NavigateTo("/login");
            return;
        }

        _currentUserId = claims.UserId;

        Lang.OnLanguageChanged += HandleLanguageChanged;

        // Start with claims (token) so we always have something to show even if DB fields are empty.
        UserFullName = $"{claims.FirstName} {claims.LastName}".Trim();
        ProfilePictureUrl = claims.ProfilePictureUrl ?? string.Empty;

        var userFromApi = await UserApiClient.GetUserByIdAsync(claims.UserId);
        if (userFromApi == null)
        {
            await AuthApiClient.LogoutAsync();
            Navigation.NavigateTo("/login");
            return;
        }

        var apiFullName = $"{userFromApi.FirstName} {userFromApi.LastName}".Trim();
        if (!string.IsNullOrWhiteSpace(apiFullName))
            UserFullName = apiFullName;

        if (!string.IsNullOrEmpty(userFromApi.Language))
        {
            Lang.SetLanguage(userFromApi.Language);
            await JSRuntime.InvokeVoidAsync("setLanguage", userFromApi.Language);
        }

        var apiProfile = userFromApi.ProfilePictureUrl ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(apiProfile))
            ProfilePictureUrl = apiProfile;
        if (!string.IsNullOrEmpty(ProfilePictureUrl))
            await JSRuntime.InvokeVoidAsync("sessionStorage.setItem", "profilePictureUrl", ProfilePictureUrl);
        else
            await JSRuntime.InvokeVoidAsync("sessionStorage.removeItem", "profilePictureUrl");

        // Load monitored people
        await LoadRecentMonitoredPeopleAsync();
        
        // Load today's measurements count
        await LoadTodayMeasurementsCountAsync();
        
        // Start auto-refresh polling (30 seconds)
        StartPolling();
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
                await Task.Delay(TimeSpan.FromSeconds(30), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await LoadRecentMonitoredPeopleAsync();
                await LoadTodayMeasurementsCountAsync();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Swallow errors to keep polling
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Lang.OnLanguageChanged -= HandleLanguageChanged;
        if (_pollingCts != null)
        {
            _pollingCts.Cancel();
            _pollingCts.Dispose();
        }
    }

    private async void HandleLanguageChanged()
    {
        await InvokeAsync(StateHasChanged);
    }

    private async Task LoadTodayMeasurementsCountAsync()
    {
        try
        {
            TodayMeasurements = await MeasurementApiClient.GetTodayMeasurementsCountAsync();
            await InvokeAsync(StateHasChanged);
        }
        catch (Exception)
        {
            TodayMeasurements = 0;
        }
    }

    private async Task LoadRecentMonitoredPeopleAsync()
    {
        try
        {
            if (_currentUserId == Guid.Empty)
            {
                MonitoredSamples = Array.Empty<MonitoredSample>();
                return;
            }

            var monitoredPeople = await UserMonitoredApiClient.GetMonitoredPeopleAsync(_currentUserId);
            if (!monitoredPeople.Any())
            {
                MonitoredSamples = Array.Empty<MonitoredSample>();
                TotalMonitored = 0;
                OfflineCount = 0;
                StableCount = 0;
                ActiveAlerts = 0;
                return;
            }

            TotalMonitored = monitoredPeople.Count;

            var cards = new List<MonitoredCardData>();

            foreach (var person in monitoredPeople)
            {
                Shared.DTOs.Responses.ESP.ESPDataResponseDTO? espData = null;
                DateTime lastMeasurementTime = DateTime.MinValue;
                
                try
                {
                    espData = await MonitoredApiClient.GetEspDataAsync(person.DeviceSerialNumber);
                }
                catch
                {
                    // If ESP data fails, continue with null data
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

                cards.Add(new MonitoredCardData
                {
                    Person = person,
                    EspData = espData,
                    LastUpdatedUtc = lastMeasurementTime
                });
            }

            // Sort by last update time (most recent first) and take top 4
            var recentCards = cards
                .OrderByDescending(c => c.LastUpdatedUtc)
                .Take(4)
                .ToList();

            MonitoredSamples = recentCards.Select(card =>
            {
                var person = card.Person;
                var espData = card.EspData;
                var heartRate = espData?.Max30100?.Count >= 1 ? espData.Max30100[0] : 0;
                var spO2Value = espData?.Max30100?.Count >= 2 ? espData.Max30100[1] : 0;
                var temperature = espData?.Temperature?.ToString("F1") ?? "N/A";
                var isOnline = espData?.IsAvailable ?? false;
                
                var status = GetStatus(heartRate, spO2Value, espData?.Temperature, isOnline);
                var lastUpdate = GetLastUpdateText(card.LastUpdatedUtc);

                var gps = espData?.Neo6m ?? T("card.noData");
                var fallDetection = !isOnline ? T("card.na") : (espData?.Mpu6050 != null ? T("card.fallStable") : T("card.noData"));
                var lastUpdateFull = card.LastUpdatedUtc != DateTime.MinValue
                    ? card.LastUpdatedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
                    : T("card.noData");
                var spO2 = spO2Value > 0 ? $"{spO2Value}%" : "N/A";

                return new MonitoredSample(
                    person.Id,
                    $"{person.FirstName} {person.LastName}",
                    status,
                    heartRate,
                    spO2,
                    temperature,
                    lastUpdate,
                    lastUpdateFull,
                    gps,
                    fallDetection,
                    isOnline
                );
            }).ToList();

            // Update statistics
            OfflineCount = MonitoredSamples.Count(m => !m.Online);
            ActiveAlerts = MonitoredSamples.Count(m => m.Status == "Critical");
            StableCount = MonitoredSamples.Count(m => m.Status == "OK");

            await InvokeAsync(StateHasChanged);
        }
        catch (Exception)
        {
            MonitoredSamples = Array.Empty<MonitoredSample>();
        }
    }

    private string GetStatus(int heartRate, int spO2, double? temperature, bool isOnline)
    {
        if (!isOnline) return "Warning";
        
        // Critical conditions
        if (heartRate < 50 || heartRate > 100)
            return "Critical";
        if (spO2 > 0 && spO2 < 90)
            return "Critical";
        if (temperature.HasValue && (temperature < 36.0 || temperature > 37.5))
            return "Critical";
        
        // Warning conditions
        if (heartRate < 60 || heartRate > 90)
            return "Warning";
        if (spO2 > 0 && spO2 < 95)
            return "Warning";
        if (temperature.HasValue && (temperature < 36.5 || temperature > 37.0))
            return "Warning";
        
        return "OK";
    }

    private string GetLastUpdateText(DateTime lastUpdate)
    {
        if (lastUpdate == DateTime.MinValue)
            return T("card.noData");

        var timeAgo = DateTime.UtcNow - lastUpdate;

        if (timeAgo.TotalMinutes < 1)
            return T("card.updatedJustNow");
        if (timeAgo.TotalMinutes < 60)
            return string.Format(T("card.updatedMin"), (int)timeAgo.TotalMinutes);
        if (timeAgo.TotalHours < 24)
            return string.Format(T("card.updatedHours"), (int)timeAgo.TotalHours);

        return string.Format(T("card.updatedDays"), (int)timeAgo.TotalDays);
    }

    private string GetInitials(string name)
    {
        var parts = name.Split(' ');
        if (parts.Length >= 2)
            return $"{parts[0][0]}{parts[1][0]}".ToUpper();
        return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpper();
    }

    protected string GetCardClass(string status, bool online)
    {
        if (!online) return "offline";
        return status.ToLower() switch
        {
            "critical" => "critical",
            "warning" => "warning",
            _ => "ok"
        };
    }

    protected string GetStatusText(string status, bool online)
    {
        if (!online) return T("card.statusOffline");
        return status.ToLower() switch
        {
            "critical" => T("card.statusAlert"),
            "warning" => T("card.statusCheckNeeded"),
            _ => T("card.statusStable")
        };
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

    protected record MonitoredSample(
        Guid Id,
        string Name,
        string Status,
        int HeartRate,
        string SpO2,
        string Temperature,
        string LastUpdate,
        string LastUpdateFull,
        string GPS,
        string FallDetection,
        bool Online);

    private void NavigateToMonitored(Guid personId)
    {
        Navigation.NavigateTo($"/monitored/{personId}");
    }

    protected async Task RequestLocation(Guid personId)
    {
        var sample = MonitoredSamples.FirstOrDefault(m => m.Id == personId);
        var gps = sample?.GPS;

        if (string.IsNullOrWhiteSpace(gps) || !TryParseGpsToLatLon(gps, out var lat, out var lon))
        {
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
        var sample = MonitoredSamples.FirstOrDefault(m => m.Id == personId);
        var name = sample?.Name ?? "Person";
        await JSRuntime.InvokeVoidAsync("alert", $"Call requested for {name}. Phone number not available.");
    }

    protected async Task TextPerson(Guid personId)
    {
        var sample = MonitoredSamples.FirstOrDefault(m => m.Id == personId);
        var name = sample?.Name ?? "Person";
        await JSRuntime.InvokeVoidAsync("alert", $"Text requested for {name}. Phone number not available.");
    }

    private sealed class MonitoredCardData
    {
        public required Domain.Entities.Monitored Person { get; init; }
        public Shared.DTOs.Responses.ESP.ESPDataResponseDTO? EspData { get; init; }
        public DateTime LastUpdatedUtc { get; init; }
    }
}