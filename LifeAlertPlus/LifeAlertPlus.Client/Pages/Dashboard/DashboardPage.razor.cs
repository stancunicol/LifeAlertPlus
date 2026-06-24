using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using LifeAlertPlus.Client.Services;
using System.Globalization;

namespace LifeAlertPlus.Client.Pages.Dashboard;

// Code-behind pentru Dashboard — afișează rezumatul stării vitale a tuturor persoanelor
// monitorizate ale utilizatorului curent (statistici agregate + ultimele 3 carduri active)
// și pornește un polling periodic pentru a ține datele la zi
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

    private int _userUpdateFrequency = 30;

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
    private bool _showOnboarding = false;
    private const string OnboardingKey = "lifealert_onboarding_done";

    // Inițializează pagina: validează autentificarea prin token, încarcă datele utilizatorului
    // (din token, apoi le suprascrie cu cele din API dacă există), aplică limba și poza de profil,
    // încarcă persoanele monitorizate + numărul de măsurători de azi, decide dacă arată onboarding-ul
    // și pornește polling-ul automat de actualizare
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

        var onboardingDone = await JSRuntime.InvokeAsync<string?>("localStorage.getItem", OnboardingKey);

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

        if (userFromApi.UpdateFrequency > 0)
            _userUpdateFrequency = userFromApi.UpdateFrequency;

        // Load monitored people and today's count in parallel
        await Task.WhenAll(LoadRecentMonitoredPeopleAsync(), LoadTodayMeasurementsCountAsync());

        // Show onboarding only when it's truly the first visit AND there are no people yet
        if (string.IsNullOrEmpty(onboardingDone) && !MonitoredSamples.Any())
            _showOnboarding = true;

        // Start auto-refresh polling (30 seconds)
        StartPolling();
    }

    // Pornește un task de fundal (fire-and-forget) care reîmprospătează periodic datele
    private void StartPolling()
    {
        _pollingCts = new CancellationTokenSource();
        _ = PollLoopAsync(_pollingCts.Token);
    }

    // Bucla de polling: la fiecare 30 secunde reîncarcă persoanele monitorizate și numărul
    // de măsurători de azi, până când componenta este distrusă (token-ul e anulat)
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

    // La distrugerea componentei: dezabonează handler-ul de schimbare a limbii și oprește polling-ul
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

    // Obține numărul total de măsurători înregistrate astăzi pentru utilizatorul curent
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

    // Încarcă toate persoanele monitorizate ale utilizatorului curent, preia în paralel
    // datele ESP (senzori) și ultima măsurătoare pentru fiecare, calculează statisticile
    // agregate (total/offline/stabile/alerte) pe baza setului complet, apoi construiește
    // lista finală cu cele mai recente 3 persoane (cu cel puțin o măsurătoare salvată)
    // pentru a fi afișate ca carduri pe dashboard
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

            // Fetch ESP data and last measurement for all people in parallel
            var cardTasks = monitoredPeople.Select(async person =>
            {
                var espTask  = FetchEspDataAsync(person.DeviceSerialNumber);
                var measTask = FetchLastMeasurementTimeAsync(person.Id);
                await Task.WhenAll(espTask, measTask);
                return new MonitoredCardData
                {
                    Person = person,
                    EspData = await espTask,
                    LastUpdatedUtc = await measTask
                };
            });
            var cards = (await Task.WhenAll(cardTasks)).ToList();

            // Statistics reflect the FULL set of monitored people, not just the
            // recent slice — otherwise the counts would silently drift below the
            // Total when fewer than 4 people had measurements.
            OfflineCount   = cards.Count(c => !IsDataCurrent(c));
            ActiveAlerts   = cards.Count(c => GetStatus(
                                ResolveHr(c.EspData), ResolveSpo2(c.EspData),
                                c.EspData?.Temperature,
                                IsDataCurrent(c),
                                c.Person.MinHeartRate, c.Person.MaxHeartRate,
                                c.Person.MinTemperature, c.Person.MaxTemperature,
                                c.Person.MinSpO2,
                                c.EspData?.IsFall ?? false) == "Critical");
            StableCount    = cards.Count(c => GetStatus(
                                ResolveHr(c.EspData), ResolveSpo2(c.EspData),
                                c.EspData?.Temperature,
                                IsDataCurrent(c),
                                c.Person.MinHeartRate, c.Person.MaxHeartRate,
                                c.Person.MinTemperature, c.Person.MaxTemperature,
                                c.Person.MinSpO2,
                                c.EspData?.IsFall ?? false) == "OK");

            // Show only the top 3 people who have at least one persisted measurement,
            // ordered by most recent. People with no data yet stay off the dashboard
            // (they're still counted in TotalMonitored / OfflineCount).
            var recentCards = cards
                .Where(c => c.LastUpdatedUtc != DateTime.MinValue)
                .OrderByDescending(c => c.LastUpdatedUtc)
                .Take(3)
                .ToList();

            MonitoredSamples = recentCards.Select(card =>
            {
                var person = card.Person;
                var espData = card.EspData;
                var heartRate = ResolveHr(espData);
                var spO2Value = ResolveSpo2(espData);
                var temperature = espData?.Temperature?.ToString("F1") ?? "N/A";
                var isOnline = IsDataCurrent(card);
                
                var status = GetStatus(heartRate, spO2Value, espData?.Temperature, isOnline,
                    person.MinHeartRate, person.MaxHeartRate,
                    person.MinTemperature, person.MaxTemperature,
                    person.MinSpO2,
                    espData?.IsFall ?? false);
                var lastUpdate = GetLastUpdateText(card.LastUpdatedUtc);

                var gps = FormatGpsLabel(espData?.Neo6m);
                var fallDetection = !isOnline ? T("card.na") : (espData?.IsFall == true ? T("card.fallPossible") : T("card.fallStable"));
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

            // (Statistics already computed above from the full cards list.)

            await InvokeAsync(StateHasChanged);
        }
        catch (Exception)
        {
            MonitoredSamples = Array.Empty<MonitoredSample>();
        }
    }

    // Determină eticheta GPS (interior/exterior) pe baza datelor brute primite de la senzor
    private string FormatGpsLabel(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return T("card.gpsIndoor");
        // NMEA "V" flag = no fix
        if (raw.Contains(",V,", StringComparison.OrdinalIgnoreCase)) return T("card.gpsIndoor");
        // Plain "lat,lon" or NMEA with valid fix
        if (TryParseGpsToLatLon(raw, out _, out _)) return T("card.gpsOutdoor");
        return T("card.gpsIndoor");
    }

    // Verifică dacă datele ESP ale unei persoane sunt "proaspete" (dispozitivul e online):
    // diferența dintre acum și ultimul timestamp trimis de senzor trebuie să fie sub
    // frecvența de actualizare configurată (a persoanei sau, implicit, a utilizatorului) + o marjă de 15s
    private bool IsDataCurrent(MonitoredCardData card)
    {
        var esp = card.EspData;
        if (esp == null || !esp.IsAvailable) return false;
        if (esp.Date <= 0) return true;
        var freq = (card.Person.UpdateFrequency ?? 0) > 0 ? card.Person.UpdateFrequency!.Value : _userUpdateFrequency;
        return (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - esp.Date) <= freq + 15L;
    }

    // Prefer the dedicated Bpm/Spo2 fields; fall back to Max30100 for firmware
    // that only populates the raw array (older versions).
    private static int ResolveHr(Shared.DTOs.Responses.ESP.ESPDataResponseDTO? d)
        => d?.Bpm ?? (d?.Max30100?.Count >= 1 ? d.Max30100[0] : 0);

    private static int ResolveSpo2(Shared.DTOs.Responses.ESP.ESPDataResponseDTO? d)
        => d?.Spo2 ?? (d?.Max30100?.Count >= 2 ? d.Max30100[1] : 0);

    // Preia datele live de la senzorul ESP al unui device, identificat prin numărul de serie
    private async Task<Shared.DTOs.Responses.ESP.ESPDataResponseDTO?> FetchEspDataAsync(string serial)
    {
        try { return await MonitoredApiClient.GetEspDataAsync(serial); }
        catch { return null; }
    }

    // Obține data/ora ultimei măsurători salvate pentru o persoană (folosită pentru sortare și status online)
    private async Task<DateTime> FetchLastMeasurementTimeAsync(Guid personId)
    {
        try
        {
            var ms = await MeasurementApiClient.GetMeasurementsByMonitoredIdAsync(personId, 1, 1);
            return ms?.FirstOrDefault()?.CreatedAt ?? DateTime.MinValue;
        }
        catch { return DateTime.MinValue; }
    }

    // Calculează statusul de sănătate (OK/Warning/Critical/Offline) pe baza puls/SpO2/temperatură
    // comparate cu pragurile personalizate ale persoanei (sau valori implicite dacă nu sunt setate),
    // plus detectarea căderii (fall detection) care are prioritate maximă
    private string GetStatus(int heartRate, int spO2, double? temperature, bool isOnline,
        int? minHr = null, int? maxHr = null,
        double? minTemp = null, double? maxTemp = null,
        int? minSpO2 = null, bool isFall = false)
    {
        if (!isOnline) return "Offline";

        int effectiveMinHr  = minHr  ?? 60;
        int effectiveMaxHr  = maxHr  ?? 100;
        double effectiveMinT = minTemp ?? 36.0;
        double effectiveMaxT = maxTemp ?? 37.5;
        int effectiveMinSpO2 = minSpO2 ?? 95;

        // Critical
        if (isFall) return "Critical";
        if (heartRate > 0 && (heartRate > effectiveMaxHr || heartRate < effectiveMinHr - 10))
            return "Critical";
        if (spO2 > 0 && spO2 < 90)
            return "Critical";
        if (temperature.HasValue && temperature > 0 && (temperature > effectiveMaxT + 0.5 || temperature < effectiveMinT - 0.5))
            return "Critical";

        // Warning
        if (heartRate > 0 && (heartRate > effectiveMaxHr - 10 || heartRate < effectiveMinHr))
            return "Warning";
        if (spO2 > 0 && spO2 < effectiveMinSpO2)
            return "Warning";
        if (temperature.HasValue && temperature > 0 && (temperature > effectiveMaxT || temperature < effectiveMinT))
            return "Warning";

        return "OK";
    }

    // Formatează diferența de timp față de ultima actualizare într-un text prietenos
    // ("acum", "X minute", "X ore", "X zile") folosind chei de traducere
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

    // Calculează inițialele afișate în avatar pe baza numelui complet
    private string GetInitials(string name)
    {
        var parts = name.Split(' ');
        if (parts.Length >= 2)
            return $"{parts[0][0]}{parts[1][0]}".ToUpper();
        return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpper();
    }

    // Mapează statusul textual la o clasă CSS folosită pentru colorarea cardului
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

    // Mapează statusul textual la mesajul tradus afișat utilizatorului
    protected string GetStatusText(string status, bool online)
    {
        if (!online) return T("card.statusOffline");
        return status.ToLower() switch
        {
            "critical" => T("card.statusCritical"),
            "warning"  => T("card.statusCheckNeeded"),
            _          => T("card.statusStable")
        };
    }

    // Calculează vârsta unei persoane monitorizate din data nașterii, ținând cont
    // dacă ziua de naștere din anul curent a trecut deja sau nu
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

    // Navighează către pagina de detalii a unei persoane monitorizate
    private void NavigateToMonitored(Guid personId)
    {
        Navigation.NavigateTo($"/monitored/{personId}");
    }

    // Marchează onboarding-ul ca fiind văzut, salvând flag-ul în localStorage (persistă între sesiuni)
    private async Task DismissOnboarding()
    {
        _showOnboarding = false;
        await JSRuntime.InvokeVoidAsync("localStorage.setItem", OnboardingKey, "1");
    }

    // Deschide locația GPS a unei persoane în Google Maps, într-un tab nou
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

    // Încearcă să extragă latitudinea și longitudinea dintr-un string GPS brut ("lat,lon"),
    // tolerant la separatorul decimal (virgulă/punct) folosind atât cultura invariantă cât și cea curentă
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

    // Container intern care grupează o persoană monitorizată cu datele ei ESP curente
    // și data ultimei măsurători — folosit pentru a evita interogări API repetate
    private sealed class MonitoredCardData
    {
        public required Domain.Entities.Monitored Person { get; init; }
        public Shared.DTOs.Responses.ESP.ESPDataResponseDTO? EspData { get; init; }
        public DateTime LastUpdatedUtc { get; init; }
    }
}