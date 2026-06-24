using LifeAlertPlus.Client.Services;
using LifeAlertPlus.Shared.DTOs.Requests.Monitored;
using LifeAlertPlus.Shared.DTOs.Responses.ESP;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Globalization;

namespace LifeAlertPlus.Client.Pages.Monitored;

// Code-behind pentru pagina Monitored — listează toate persoanele monitorizate ale
// utilizatorului curent (active și arhivate), afișează statusul lor de sănătate în timp real
// (polling), și permite adăugare, arhivare, restaurare și ștergere de persoane
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
    
    // Lista de carduri afișată în UI — aplică succesiv filtrul de căutare după nume,
    // filtrul de status (Critical/Warning/OK) și filtrul online/offline
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

    // Inițializează pagina: se abonează la schimbarea limbii, încarcă utilizatorul din token,
    // apoi încarcă în paralel lista persoanelor active și pe cea a persoanelor arhivate
    // (paralel pentru ca numărul afișat pe tab-ul de arhivă să fie corect de la început),
    // și pornește polling-ul pentru actualizarea datelor ESP
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

    // Extrage datele utilizatorului din claim-urile token-ului JWT, apoi le completează/suprascrie
    // cu valorile din API (nume, poză de profil, frecvența de actualizare configurată)
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

    // Încarcă lista persoanelor monitorizate active (nearhivate) ale utilizatorului
    // și reîmprospătează imediat datele ESP asociate
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

    // Încarcă lista persoanelor arhivate (date statice, fără polling pentru ele)
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

    // Comută între vizualizarea persoanelor active și cea a persoanelor arhivate;
    // oprește polling-ul când se intră în arhivă (date statice) și îl repornește la revenire
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

    // Deschide modalul de confirmare pentru arhivarea persoanei selectate
    private void OpenArchiveConfirm(LifeAlertPlus.Domain.Entities.Monitored person)
    {
        _personPendingAction = person;
        _actionError = string.Empty;
        _showArchiveConfirm = true;
    }

    // Deschide modalul de confirmare pentru restaurarea unei persoane arhivate
    private void OpenRestoreConfirm(LifeAlertPlus.Domain.Entities.Monitored person)
    {
        _personPendingAction = person;
        _actionError = string.Empty;
        _showRestoreConfirm = true;
    }

    // Deschide modalul de confirmare pentru ștergerea definitivă (din arhivă)
    private void OpenDeleteConfirm(LifeAlertPlus.Domain.Entities.Monitored person)
    {
        _personPendingAction = person;
        _actionError = string.Empty;
        _showDeleteConfirm = true;
    }

    // Deschide modalul de confirmare pentru ștergerea "soft" (din lista activă, fără arhivare explicită)
    private void OpenSoftDeleteConfirm(LifeAlertPlus.Domain.Entities.Monitored person)
    {
        _personPendingAction = person;
        _actionError = string.Empty;
        _showSoftDeleteConfirm = true;
    }

    // Închide orice modal de acțiune deschis și resetează starea aferentă
    private void CloseActionModal()
    {
        _showArchiveConfirm = false;
        _showRestoreConfirm = false;
        _showDeleteConfirm = false;
        _showSoftDeleteConfirm = false;
        _personPendingAction = null;
        _actionError = string.Empty;
    }

    // Confirmă arhivarea persoanei selectate prin API, apoi reîncarcă lista de persoane active
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

    // Confirmă restaurarea persoanei din arhivă prin API, apoi reîncarcă lista de arhivă
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

    // Confirmă ștergerea definitivă a persoanei (din arhivă) prin API, apoi reîncarcă lista de arhivă
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

    // Confirmă ștergerea "soft" a persoanei direct din lista activă, apoi reîncarcă lista activă
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

    // Formatează data arhivării pentru afișare ("—" dacă nu există)
    private static string FormatArchivedDate(DateTime? archivedAt)
        => archivedAt.HasValue ? archivedAt.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm") : "—";

    // Pornește un task de fundal (fire-and-forget) care reîmprospătează periodic datele ESP
    private void StartPolling()
    {
        _pollingCts = new CancellationTokenSource();
        _ = PollLoopAsync(_pollingCts.Token);
    }

    // Bucla de polling: reîmprospătează datele ESP, apoi așteaptă 30 secunde, repetând
    // până la anularea token-ului (la dispose sau la comutarea pe vizualizarea de arhivă)
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

    // Pentru fiecare persoană monitorizată activă, preia în paralel datele ESP curente
    // și data ultimei măsurători salvate, construind cardurile afișate în UI
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

    // Preia datele live de la senzorul ESP al unui device, identificat prin numărul de serie
    private async Task<ESPDataResponseDTO?> FetchEspAsync(string serial, CancellationToken token)
    {
        try { return await MonitoredApiClient.GetEspDataAsync(serial, token); }
        catch (TaskCanceledException) { throw; }
        catch { return null; }
    }

    // Obține data/ora ultimei măsurători salvate pentru o persoană
    private async Task<DateTime> FetchLastMeasurementTimeAsync(Guid personId)
    {
        try
        {
            var ms = await MeasurementApiClient.GetMeasurementsByMonitoredIdAsync(personId, 1, 1);
            return ms?.FirstOrDefault()?.CreatedAt ?? DateTime.MinValue;
        }
        catch { return DateTime.MinValue; }
    }

    // Calculează inițialele afișate în avatar pe baza numelui complet
    private string GetInitials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2)
        {
            return $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant();
        }

        return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpperInvariant();
    }

    // Mapează statusul textual la o clasă CSS folosită pentru colorarea badge-ului/cardului
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

    // Mapează statusul textual la mesajul tradus afișat utilizatorului
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

    // Deschide modalul de adăugare a unei noi persoane monitorizate, cu un formular nou/resetat
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

    // Validează câmpurile obligatorii și trimite cererea de adăugare a unei noi persoane
    // monitorizate; la succes reîncarcă lista și închide modalul
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

    // Formatează o listă de valori întregi (ex: citiri brute) pentru afișare, separate prin virgulă
    private string FormatIntList(IEnumerable<int>? values)
    {
        return values == null ? "N/A" : string.Join(", ", values);
    }

    // Formatează datele GPS brute (potențial multi-linie) pentru afișare, separate prin " / "
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

    // Formatează data ultimei măsurători salvate pentru un card, în ora locală
    private string FormatLastUpdate(MonitoredCard card)
    {
        if (card.LastUpdatedUtc == DateTime.MinValue)
            return "No data";

        return card.LastUpdatedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
    }

    // Interpretează timestamp-ul brut trimis de firmware: dacă pare un Unix timestamp valid
    // (> 1 miliard) îl convertește în dată/oră locală; altfel îl tratează ca timp relativ (secunde de la pornire)
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

    // Extrage pulsul (BPM) din array-ul brut Max30100 returnat de senzor (poziția 0)
    private string GetPulse(ESPDataResponseDTO? data)
    {
        if (data?.Max30100 == null || data.Max30100.Count == 0)
        {
            return "N/A";
        }

        var pulse = data.Max30100[0];
        return pulse > 0 ? pulse.ToString() : "N/A";
    }

    // Extrage saturația de oxigen (SpO2) din array-ul brut Max30100 returnat de senzor (poziția 1)
    private string GetOxygen(ESPDataResponseDTO? data)
    {
        if (data?.Max30100 == null || data.Max30100.Count < 2)
        {
            return "N/A";
        }

        var spo2 = data.Max30100[1];
        return spo2 > 0 ? spo2.ToString() : "N/A";
    }

    // Formatează temperatura corporală citită de senzor cu o zecimală
    private string GetTemperature(ESPDataResponseDTO? data)
    {
        if (data?.Temperature != null)
        {
            return data.Temperature.Value.ToString("F1");
        }
        return "N/A";
    }

    // Determină eticheta GPS (interior/exterior) pe baza formatului datelor brute primite
    // de la senzor: poate fi string simplu "lat,lon" sau propoziție NMEA ($GPRMC/$GPGLL)
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

    // Determină textul de risc de cădere afișat: "offline" dacă nu sunt date recente,
    // altfel pe baza deciziei firmware-ului (IsFallEvent)
    private string FormatFallRisk(MonitoredCard card)
    {
        if (!IsDataCurrent(card)) return T("card.statusOffline");
        return IsFallEvent(card) ? T("card.fallPossible") : T("card.fallStable");
    }

    // Determină frecvența de actualizare efectivă: cea configurată pe persoană, altfel
    // valoarea implicită setată de utilizator
    private int GetEffectiveUpdateFrequency(LifeAlertPlus.Domain.Entities.Monitored person)
        => (person.UpdateFrequency ?? 0) > 0 ? person.UpdateFrequency!.Value : _userUpdateFrequency;

    // Verifică dacă datele ESP ale unei persoane sunt "proaspete" (dispozitivul e online):
    // diferența dintre acum și ultimul timestamp trimis de senzor trebuie să fie sub
    // frecvența de actualizare efectivă + o marjă de 15 secunde
    private bool IsDataCurrent(MonitoredCard card)
    {
        var esp = card.LastData;
        if (esp == null || !esp.IsAvailable) return false;
        if (esp.Date <= 0) return true;
        var threshold = GetEffectiveUpdateFrequency(card.Person) + 15L;
        return (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - esp.Date) <= threshold;
    }

    // Calculează statusul de sănătate al unui card (Critical/Warning/OK/NoData) pe baza
    // puls/SpO2/temperatură comparate cu pragurile personalizate ale persoanei (sau valori
    // implicite), plus detectarea căderii care are prioritate maximă
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

    // Combină statusul cardului cu clasa CSS corespunzătoare, pentru afișare directă în UI
    private string GetCardStatusClass(MonitoredCard card)
    {
        return GetStatusClass(GetCardStatus(card));
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

    // Navighează către pagina de detalii a unei persoane monitorizate
    private void ViewDetails(Guid personId)
    {
        NavigationManager.NavigateTo($"/monitored/{personId}");
    }

    // Deschide locația GPS a unei persoane în Google Maps, într-un tab nou
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

    // Funcționalitate de apel telefonic — neimplementată încă, afișează un alert temporar;
    // numărul de telefon nu este stocat în entitatea Monitored (câmp lipsă în model)
    protected async Task CallPerson(Guid personId)
    {
        var card = _monitoredCards.FirstOrDefault(c => c.Person.Id == personId);
        var name = card?.Person != null ? $"{card.Person.FirstName} {card.Person.LastName}" : "Person";
        await JSRuntime.InvokeVoidAsync("alert", $"Call requested for {name}. Phone number not available.");
    }

    // Funcționalitate de trimitere SMS — neimplementată încă, afișează un alert temporar;
    // același motiv ca CallPerson: numărul de telefon lipsește din modelul de date curent
    protected async Task TextPerson(Guid personId)
    {
        var card = _monitoredCards.FirstOrDefault(c => c.Person.Id == personId);
        var name = card?.Person != null ? $"{card.Person.FirstName} {card.Person.LastName}" : "Person";
        await JSRuntime.InvokeVoidAsync("alert", $"Text requested for {name}. Phone number not available.");
    }

    // La distrugerea componentei: dezabonează handler-ul de schimbare a limbii și oprește polling-ul
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

    // Container intern care grupează o persoană monitorizată cu datele ei ESP curente
    // și data ultimei măsurători — folosit pentru a evita interogări API repetate
    private sealed class MonitoredCard
    {
        public required LifeAlertPlus.Domain.Entities.Monitored Person { get; init; }
        public ESPDataResponseDTO? LastData { get; init; }
        public DateTime LastUpdatedUtc { get; init; }
    }
}