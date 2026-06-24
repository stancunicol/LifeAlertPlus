using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Globalization;
using System.Net.Http.Json;
using LifeAlertPlus.Client.Services;
using LifeAlertPlus.Shared.DTOs.Responses.Measurement;
using LifeAlertPlus.Shared.DTOs.Requests.AI;
using LifeAlertPlus.Shared.DTOs.Responses.AI;
using LifeAlertPlus.Shared.DTOs.Requests.Email;
using LifeAlertPlus.Shared.DTOs.Requests.Monitored;
using LifeAlertPlus.Shared.DTOs.Responses.ActivityProfile;
using LifeAlertPlus.Shared.DTOs.Responses.Monitoring;
using LifeAlertPlus.Shared.DTOs.Responses.Wifi;
using LifeAlertPlus.Shared.DTOs.Responses.DoctorNote;

namespace LifeAlertPlus.Client.Pages.SelectedMonitored
{
    // Code-behind pentru pagina principală de detaliu/editare a unei persoane monitorizate.
    // Cea mai complexă pagină din Client: afișează măsurători live (HR/Temp/SpO2), grafice
    // istorice (zilnic/săptămânal), praguri vitale, predicții AI, rețele WiFi ale device-ului ESP,
    // notițe medic, export/raport email, arhivare și ștergere a persoanei monitorizate.
    public partial class SelectedMonitored : ComponentBase, IAsyncDisposable
    {
        // Id-ul persoanei monitorizate, primit ca parametru de rută/query.
        [Parameter]
        public Guid PersonId { get; set; }

        [Inject]
        private NavigationManager NavigationManager { get; set; } = default!;

        [Inject]
        private MonitoredApiClient MonitoredApiClient { get; set; } = default!;

        [Inject]
        private TokenParserService TokenParserService { get; set; } = default!;

        [Inject]
        private MeasurementApiClient MeasurementApiClient { get; set; } = default!;

        [Inject]
        private UserApiClient UserApiClient { get; set; } = default!;

        [Inject]
        private AIPredictionService AIPredictionService { get; set; } = default!;

        [Inject]
        private IJSRuntime JSRuntime { get; set; } = default!;

        [Inject]
        private PushNotificationClientService PushService { get; set; } = default!;

        [Inject]
        private NotificationService NotificationSvc { get; set; } = default!;

        [Inject]
        private LanguageService Lang { get; set; } = default!;

        [Inject]
        private HttpClient HttpClient { get; set; } = default!;

        [Inject]
        private WifiApiClient WifiApiClient { get; set; } = default!;

        // Scurtătură pentru traducerea textelor din UI prin LanguageService.
        private string T(string key) => Lang.T(key);

        private ElementReference _hrSvgRef;
        private ElementReference _tempSvgRef;
        private ElementReference _spo2SvgRef;
        private ElementReference _hrScrollRef;
        private ElementReference _tempScrollRef;
        private ElementReference _mapRef;
        private bool _tooltipsInitialized;
        private bool _mapInitialized;
        private bool _scrollSyncInitialized;

        private DayOfWeek _firstDayOfWeek = DayOfWeek.Monday;
        // Datele complete ale persoanei monitorizate (informații personale, praguri, device etc.)
        private PersonDetail? Person { get; set; }
        private bool IsLoading { get; set; } = true;
        // Ultimele date primite de la device-ul ESP (telemetrie live: poziție, status conexiune etc.)
        private LifeAlertPlus.Shared.DTOs.Responses.ESP.ESPDataResponseDTO? _espData;
        private string? LoadError { get; set; }

        // Istoricul brut al măsurătorilor (puncte cu valoare + timestamp) pentru fiecare grafic.
        private List<ChartDataPoint> HeartRateHistory { get; set; } = new();
        private List<ChartDataPoint> TemperatureHistory { get; set; } = new();
        private List<ChartDataPoint> SpO2History { get; set; } = new();
        // Coordonatele (X, Y) deja proiectate pe viewBox-ul SVG, recalculate la zoom/schimbare interval.
        private List<(double X, double Y)> HeartRatePoints { get; set; } = new();
        private List<(double X, double Y)> TemperaturePoints { get; set; } = new();
        private List<(double X, double Y)> SpO2Points { get; set; } = new();
        // Date pentru tooltip-urile interactive afișate la hover pe puncte din grafic.
        private List<TooltipPoint> HrTooltipData { get; set; } = new();
        private List<TooltipPoint> TempTooltipData { get; set; } = new();
        private List<TooltipPoint> SpO2TooltipData { get; set; } = new();
        private List<Alert> RecentAlerts { get; set; } = new();
        private List<Measurement> RecentMeasurements { get; set; } = new();
        // Predicție AI (ex. risc de eveniment medical) pentru persoana monitorizată.
        private AIPredictionResponseDTO? AIPrediction { get; set; }
        private bool AIPredictionLoading { get; set; }
        private bool _showProbabilities = false;
        // Predicții de tendință (trend) pe baza istoricului de monitorizare.
        private LifeAlertPlus.Shared.DTOs.Responses.Monitoring.TrendPredictionResponseDTO? TrendPredictions { get; set; }
        private bool TrendPredictionsLoading { get; set; }
        private ActivityProfileResponseDTO? ActivityProfile { get; set; }
        private bool ActivityProfileLoading { get; set; }
        private string UserFullName = "";
        private string ProfilePictureUrl = "";
        // Mod de vizualizare curent al graficelor: zilnic sau săptămânal.
        private ChartViewMode CurrentChartView { get; set; } = ChartViewMode.Daily;
        private int _weekOffset = 0; // 0 = current week, -1 = previous week, etc.
        private string _chartWeekLabel = "";
        private bool _hasPrevWeekData = false;
        private int _dayOffset = 0; // 0 = today, -1 = yesterday, etc.
        // Seturi folosite pentru a marca în UI (ex. calendar/navigare) zilele/săptămânile care au date.
        private HashSet<DateTime> _daysWithData = new();
        private HashSet<DateTime> _weeksWithData = new(); // week-start dates
        private string _chartDayLabel = "";
        private bool _hasPrevDayData = false;
        // Timer pentru reîmprospătarea periodică (polling) a datelor live ale paginii.
        private System.Threading.Timer? _refreshTimer;
        private bool _disposed = false;

        // ── False-alarm feedback ──────────────────────────────────────────────
        // Feedback în așteptare: utilizatorul este întrebat dacă o alertă anterioară a fost reală
        // sau o falsă alarmă, pentru a ajuta la calibrarea/încrederea în sistemul de alertare.
        private LifeAlertPlus.Shared.DTOs.Responses.Notification.PendingFeedbackDTO? _pendingFeedback;
        private bool _feedbackSubmitting;

        // Verifică dacă există un feedback de confirmare în așteptare pentru această persoană monitorizată.
        // Flux: NotificationSvc.GetPendingFeedbackAsync()
        //   → HTTP GET api/notification/pending-feedback
        //   → NotificationController → DB (notificări fără feedback)
        //   → filtrare locală pe PersonId → _pendingFeedback
        private async Task LoadPendingFeedbackAsync()
        {
            try
            {
                var all = await NotificationSvc.GetPendingFeedbackAsync();
                _pendingFeedback = all.FirstOrDefault(f => f.IdMonitored == PersonId);
            }
            catch { _pendingFeedback = null; }
            await InvokeAsync(StateHasChanged);
        }

        // Trimite răspunsul utilizatorului (alertă reală sau falsă alarmă) către server.
        // Flux: NotificationSvc.SubmitFeedbackAsync(id, wasReal)
        //   → HTTP PATCH api/notification/{id}/feedback { WasReal: true/false }
        //   → NotificationController → DB (câmpul WasReal actualizat pe notificare)
        private async Task SubmitFeedbackAsync(bool wasReal)
        {
            if (_pendingFeedback == null || _feedbackSubmitting) return;
            _feedbackSubmitting = true;
            try
            {
                await NotificationSvc.SubmitFeedbackAsync(_pendingFeedback.Id, wasReal);
                _pendingFeedback = null;
            }
            finally { _feedbackSubmitting = false; StateHasChanged(); }
        }

        // Ascunde bannerul de feedback fără a trimite un răspuns la server.
        private void DismissFeedback()
        {
            _pendingFeedback = null;
            StateHasChanged();
        }
        private int _refreshInFlight; // 0 = idle, 1 = a RefreshDataAsync is currently running
        private DateTime _lastRefreshUtc = DateTime.MinValue;
        private static readonly TimeSpan MinRefreshInterval = TimeSpan.FromSeconds(5);

        // Chart horizontal zoom — scales ONLY the X axis. The viewBox X width grows with
        // zoom (so the data spreads over more SVG units), and the CSS width follows
        // proportionally so the rendered text/curve size stays unchanged. Height stays at
        // 275 px regardless of zoom, so the card never grows vertically.
        private double _chartZoom = 1.0;
        private const double ChartMinZoom = 1.0;
        private const double ChartMaxZoom = 4.0;
        private const double ChartZoomStep = 0.5;
        private const double ChartViewBoxBaseWidth = 2400;
        private const double ChartViewBoxHeight = 200;
        private const double ChartCssHeight = 275;
        private const double ChartPaddingLeft = 90;
        private const double ChartPaddingRight = 15;
        private const double ChartCssScale = ChartCssHeight / ChartViewBoxHeight; // 1.375 — preserved aspect

        // Lățimea viewBox-ului SVG crește proporțional cu nivelul de zoom curent.
        private double ChartViewBoxWidth => ChartViewBoxBaseWidth * _chartZoom;
        // Lățimea CSS efectivă a graficului, calculată astfel încât proporțiile vizuale să rămână constante.
        private double ChartCssWidth => ChartViewBoxWidth * ChartCssScale;

        private string ChartViewBoxAttr =>
            $"0 0 {ChartViewBoxWidth.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)} {ChartViewBoxHeight.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)}";

        private string ChartSvgStyle =>
            $"width:{ChartCssWidth.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)}px;"
            + $"height:{ChartCssHeight.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)}px;";

        // Right edge of the chart area inside the viewBox (used by grid lines, axis line).
        private string ChartRightEdge =>
            (ChartViewBoxWidth - ChartPaddingRight).ToString("F0", System.Globalization.CultureInfo.InvariantCulture);

        // Width of the chart background rect inside the viewBox.
        private string ChartBgWidth =>
            (ChartViewBoxWidth - ChartPaddingLeft - ChartPaddingRight).ToString("F0", System.Globalization.CultureInfo.InvariantCulture);

        private string ChartZoomLabel => $"{(int)Math.Round(_chartZoom * 100)}%";

        // Crește nivelul de zoom al graficelor (cu pas fix), limitat la ChartMaxZoom.
        private async Task ZoomChartIn()
        {
            if (_chartZoom >= ChartMaxZoom) return;
            _chartZoom = Math.Min(ChartMaxZoom, _chartZoom + ChartZoomStep);
            RecomputeChartPoints();
            await InvokeAsync(StateHasChanged);
            await InitTooltipsAsync();
        }

        // Reduce nivelul de zoom al graficelor, limitat la ChartMinZoom (nu se poate micșora sub 100%).
        private async Task ZoomChartOut()
        {
            if (_chartZoom <= ChartMinZoom) return;
            _chartZoom = Math.Max(ChartMinZoom, _chartZoom - ChartZoomStep);
            RecomputeChartPoints();
            await InvokeAsync(StateHasChanged);
            await InitTooltipsAsync();
        }

        // Resetează zoom-ul graficelor la valoarea implicită (100%).
        private async Task ResetChartZoom()
        {
            _chartZoom = 1.0;
            RecomputeChartPoints();
            await InvokeAsync(StateHasChanged);
            await InitTooltipsAsync();
        }

        // Re-project HeartRateHistory / TemperatureHistory / SpO2History onto the new (zoom-aware) X axis.
        // No re-fetch from the API is needed — the underlying data hasn't changed.
        private void RecomputeChartPoints()
        {
            HeartRatePoints = ComputePointsWithRange(HeartRateHistory, 40, 140);
            TemperaturePoints = ComputePointsWithRange(TemperatureHistory, 35, 39);
            SpO2Points = ComputePointsWithRange(SpO2History, 85, 100);
            HrTooltipData = ComputeTooltipData(HeartRateHistory, 40, 120);
            TempTooltipData = ComputeTooltipData(TemperatureHistory, 35, 39);
            SpO2TooltipData = ComputeTooltipData(SpO2History, 85, 100);
            _tooltipsInitialized = false;
        }

        // Handler apelat când serviciul de push notifications primește un mesaj live —
        // declanșează o reîmprospătare a datelor paginii (măsurători/alerte noi).
        private void OnPushNotificationReceived(string message, string severity)
        {
            if (_disposed)
                return;

            _ = InvokeAsync(async () =>
            {
                await RefreshDataAsync();
            });
        }

        // Handler apelat când a fost adăugată o măsurătoare nouă pentru persoana monitorizată curentă —
        // ignoră evenimentul dacă vine pentru altă persoană (monitoredId diferit).
        private void OnMeasurementAdded(Guid monitoredId)
        {
            if (_disposed || monitoredId != PersonId)
                return;

            _ = InvokeAsync(async () =>
            {
                await RefreshDataAsync();
            });
        }

        // User global vital range fallbacks
        // Valori implicite (fallback) ale pragurilor vitale ale utilizatorului — folosite când
        // persoana monitorizată nu are praguri personalizate setate la nivel de profil/afecțiune.
        private int _userMinHr = 60;
        private int _userMaxHr = 100;
        private double _userMinTemp = 36.0;
        private double _userMaxTemp = 37.5;
        private int _userUpdateFrequency = 30;

        private bool _isCurrentDataFresh;
        private bool _isLastKnownGps;

        // Determină dacă datele ESP curente sunt "proaspete" (recente), comparând timestamp-ul
        // ultimei transmisii cu frecvența de actualizare configurată + o marjă de toleranță de 15s.
        private bool IsEspDataFresh(Shared.DTOs.Responses.ESP.ESPDataResponseDTO? esp, int updateFrequencySeconds)
        {
            if (esp == null || !esp.IsAvailable) return false;
            if (esp.Date <= 0) return true;
            return (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - esp.Date) <= updateFrequencySeconds + 15L;
        }

        // Edit modal state
        // Stare pentru modalul de editare a datelor persoanei monitorizate (date personale,
        // praguri vitale, device, retenție date) — câmpurile editabile sunt populate din Person.
        private bool _showEditModal;
        // control whether edit button is shown (can be toggled via query param)
        private bool _showEditButton = true;
        private bool _isSaving;
        private string? _editError;
        private string _editFirstName = "";
        private string _editLastName = "";
        private string _editGender = "";
        private DateTime? _editBirthdate;
        private string _editAddress = "";
        private string _editDeviceSerial = "";
        private int? _editMinHr;
        private int? _editMaxHr;
        private double? _editMinTemp;
        private double? _editMaxTemp;
        private int? _editMinSpO2;
        private int? _editMaxSpO2;
        private int? _editUpdateFrequency;
        private int? _editDataRetentionDays;
        private int? _editArchiveRetentionDays;

        // Condition threshold preview (client-side mirror of ConditionThresholdAdjuster)
        // Praguri vitale "previzualizate" pe client — oglindă a logicii server-side din
        // ConditionThresholdAdjuster, folosită pentru a arăta utilizatorului în timp real cum
        // afecțiunile selectate ajustează intervalele normale de HR/Temp/SpO2, fără un apel API.
        private int _previewMinHr = 60;
        private int _previewMaxHr = 100;
        private double _previewMinTemp = 36.0;
        private double _previewMaxTemp = 37.5;
        private int _previewMinSpO2 = 95;
        private int _previewMaxSpO2 = 100;

        // Export modal state
        private bool _showExportModal;
        private bool _isExporting;
        private DateTime? _exportStartDate;
        private DateTime? _exportEndDate;
        private DateTime? _exportMinDate;
        private DateTime? _exportMaxDate;
        private int? _exportMeasurementCount;
        private int _exportDistinctDays;

        // Email modal state (report)
        private bool _showEmailModal;
        private bool _isSendingEmail;
        private string _doctorEmail = string.Empty;
        private string? _emailStatusMessage;
        private bool _emailSuccess;

        // Invitation modal state (doctor invite link)
        private bool _showInviteModal;
        private bool _isSendingInvitation;
        private string _inviteDoctorEmail = string.Empty;
        private string? _inviteStatusMessage;
        private bool _inviteSuccess;

        // WiFi modal state (multiple networks for the ESP device)
        // Stare pentru modalul de configurare a rețelelor WiFi memorate de device-ul ESP
        // (dispozitivul poate avea mai multe rețele salvate, limitate la MaxWifiNetworks).
        private bool _showWifiModal;
        private bool _isWifiLoading;
        private bool _isAddingWifi;
        private List<WifiNetworkResponseDTO> _wifiNetworks = new();
        private string _newWifiSsid = string.Empty;
        private string _newWifiPassword = string.Empty;
        private string? _wifiStatusMessage;
        private bool _wifiSuccess;
        private const int MaxWifiNetworks = 3;

        // Doctor notes state
        private List<DoctorNoteDTO> _doctorNotes = new();
        private bool _doctorNotesLoading;
        private bool _showDoctorNotesModal;
        private string _doctorNoteContent = string.Empty;
        private bool _isSavingDoctorNote;
        private bool _isDoctorUser = false; // True if accessing via /doctor/patient route

        // Delete modal state
        // Stare pentru modalul de confirmare a ștergerii (soft-delete/arhivare) persoanei monitorizate.
        private bool _showDeleteModal;
        private bool _isDeleting;
        private string? _deleteError;

        // Referință statică la instanța curentă a paginii — folosită probabil pentru a permite
        // altor componente/servicii (ex. callback-uri JS interop) să acceseze pagina activă.
        private static SelectedMonitored? _instance;

        private enum ChartViewMode
        {
            Daily,
            Weekly
        }

        // Încarcă datele complete ale persoanei monitorizate de la API și pregătește starea inițială a paginii.
        private async Task LoadPersonDataAsync()
        {
            IsLoading = true;
            LoadError = null;

            try
            {
                // Pas 1: detaliile persoanei monitorizate (date personale, device, praguri configurate).
                var monitored = await MonitoredApiClient.GetMonitoredPersonByIdAsync(PersonId);
                if (monitored == null)
                {
                    Person = null;
                    LoadError = "Monitored person not found.";
                    IsLoading = false;
                    return;
                }

                // Get ESP data (stored as field so the device diagnostics card can access it)
                // Pas 2: ultima telemetrie live a device-ului ESP asociat acestei persoane.
                var espData = await MonitoredApiClient.GetEspDataAsync(monitored.DeviceSerialNumber);
                _espData = espData;
                // Verifică dacă datele ESP sunt suficient de recente (în limita frecvenței de update + marjă),
                // altfel persoana e considerată "Offline" și nu se afișează valori live înșelătoare.
                _isCurrentDataFresh = IsEspDataFresh(espData, monitored.UpdateFrequency ?? _userUpdateFrequency);

                int heartRate = 0;
                int spO2 = 0;
                double temperature = 0;
                string gps = "No data";
                string status = "OK";

                if (_isCurrentDataFresh)
                {
                    // Date live valide — extrage valorile vitale curente și calculează statusul
                    // (OK/Warning/Critical) pe baza pragurilor persoanei și a afecțiunilor cunoscute.
                    heartRate = espData!.Bpm ?? 0;
                    spO2 = espData.Spo2 ?? 0;
                    temperature = espData.Temperature ?? 0;
                    gps = espData.Neo6m ?? "No data";

                    status = ComputeStatus(
                        heartRate, spO2, temperature,
                        espData.IsFall,
                        monitored.MinHeartRate ?? _userMinHr,
                        monitored.MaxHeartRate ?? _userMaxHr,
                        monitored.MinTemperature ?? _userMinTemp,
                        monitored.MaxTemperature ?? _userMaxTemp,
                        monitored.MinSpO2 ?? 95,
                        _conditions);
                }
                else
                {
                    // Fără date live recente — nu putem evalua starea vitală curentă, deci statusul e "Offline".
                    status = "Offline";
                }

                // Get last measurement time
                // Ultima măsurătoare salvată este folosită doar pentru afișarea momentului ultimei actualizări
                // și, dacă datele live sunt expirate, ca sursă de fallback pentru poziția GPS.
                var measurements = await MeasurementApiClient.GetMeasurementsByMonitoredIdAsync(monitored.Id, 1, 1);
                var lastMeasurement = measurements?.FirstOrDefault();
                string lastUpdate = lastMeasurement != null
                    ? lastMeasurement.CreatedAt.ToLocalTime().ToString("MMMM dd, yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture)
                    : "No data";

                // When live data is stale, use the last stored GPS coordinates as fallback
                _isLastKnownGps = false;
                if (!_isCurrentDataFresh && !string.IsNullOrWhiteSpace(lastMeasurement?.Coordinates))
                {
                    gps = lastMeasurement.Coordinates;
                    _isLastKnownGps = true;
                }

                Person = new PersonDetail
                {
                    Id = monitored.Id,
                    Name = $"{monitored.FirstName} {monitored.LastName}".Trim(),
                    FirstName = monitored.FirstName ?? "",
                    LastName = monitored.LastName ?? "",
                    Age = GetAge(monitored.Birthdate),
                    HeartRate = heartRate,
                    SpO2 = spO2,
                    Temperature = temperature,
                    GPS = gps,
                    Status = status,
                    LastUpdate = lastUpdate,
                    Location = string.IsNullOrWhiteSpace(monitored.Address) ? "N/A" : monitored.Address,
                    DeviceSerial = monitored.DeviceSerialNumber,
                    MinHeartRate = monitored.MinHeartRate ?? _userMinHr,
                    MaxHeartRate = monitored.MaxHeartRate ?? _userMaxHr,
                    MinTemperature = monitored.MinTemperature ?? _userMinTemp,
                    MaxTemperature = monitored.MaxTemperature ?? _userMaxTemp,
                    MinSpO2 = monitored.MinSpO2 ?? 95,
                    MaxSpO2 = monitored.MaxSpO2 ?? 100,
                    UpdateFrequency = monitored.UpdateFrequency ?? _userUpdateFrequency,
                    IsArchived = monitored.IsArchived,
                    ArchivedAt = monitored.ArchivedAt,
                    DeletedAt = monitored.DeletedAt,
                    DataRetentionDays = monitored.DataRetentionDays,
                    ArchiveRetentionDays = monitored.ArchiveRetentionDays
                };

                // Skip live/predictive features for archived or soft-deleted persons.
                // Persoanele arhivate sau șterse (soft-delete) nu mai au monitorizare activă, deci
                // predicțiile AI/trend/profil de activitate nu se mai încarcă (ar fi date irelevante/eronate).
                if (!monitored.IsArchived && monitored.DeletedAt == null)
                {
                    if (_isCurrentDataFresh && espData != null)
                        _ = LoadAIPredictionAsync(espData);
                    else
                        AIPrediction = null;
                    _ = LoadTrendPredictionsAsync(PersonId);
                    _ = LoadActivityProfileAsync(PersonId);
                }
                // Afecțiunile și notițele medicului se încarcă indiferent de starea activă/arhivată,
                // fiind informații istorice relevante oricând.
                _ = LoadConditionsAsync(PersonId);
                _ = LoadDoctorNotesAsync(PersonId);

                IsLoading = false;
            }
            catch (Exception ex)
            {
                LoadError = $"Error loading data: {ex.Message}";
                IsLoading = false;
            }
        }

        // Calculează vârsta în ani împliniți pe baza datei nașterii (ajustează dacă ziua de naștere
        // din anul curent nu a trecut încă).
        private int GetAge(DateTime? birthdate)
        {
            if (!birthdate.HasValue)
            {
                return 0;
            }

            var today = DateTime.Today;
            var age = today.Year - birthdate.Value.Year;
            if (birthdate.Value.Date > today.AddYears(-age))
            {
                age--;
            }

            return age;
        }

        // Construiește cererea de predicție AI din ultimele date senzoriale live (puls, temperatură,
        // SpO2, accelerometru, giroscop) și declanșează predicția.
        private async Task LoadAIPredictionAsync(LifeAlertPlus.Shared.DTOs.Responses.ESP.ESPDataResponseDTO espData)
        {
            var request = new AIPredictionRequestDTO
            {
                MonitoredId = PersonId,
                Pulse = espData.Bpm ?? (espData.Max30100?.Count >= 1 ? espData.Max30100[0] : 0),
                Temperature = espData.Temperature ?? 0,
                Spo2 = espData.Spo2 ?? (espData.Max30100?.Count >= 2 ? espData.Max30100[1] : 97.0),
                AccelX = espData.Mpu6050 != null && espData.Mpu6050.Count >= 1 ? espData.Mpu6050[0] : 0,
                AccelY = espData.Mpu6050 != null && espData.Mpu6050.Count >= 2 ? espData.Mpu6050[1] : 0,
                AccelZ = espData.Mpu6050 != null && espData.Mpu6050.Count >= 3 ? espData.Mpu6050[2] : 0,
                GyroX = espData.Gyro != null && espData.Gyro.Count >= 1 ? espData.Gyro[0] : 0,
                GyroY = espData.Gyro != null && espData.Gyro.Count >= 2 ? espData.Gyro[1] : 0,
                GyroZ = espData.Gyro != null && espData.Gyro.Count >= 3 ? espData.Gyro[2] : 0,
            };
            await RunAIPredictionAsync(request);
        }

        // Variantă de fallback: rulează predicția AI folosind ultima măsurătoare salvată în bază
        // (utilă când nu există date live ESP disponibile).
        private async Task LoadAIPredictionFromLastMeasurementAsync()
        {
            try
            {
                var measurements = await MeasurementApiClient.GetMeasurementsByMonitoredIdAsync(PersonId, 1, 1);
                var last = measurements?.FirstOrDefault();
                if (last == null) return;

                var request = new AIPredictionRequestDTO
                {
                    MonitoredId = PersonId,
                    Pulse = last.Pulse,
                    Temperature = last.Temperature,
                    Spo2 = last.SpO2 > 0 ? last.SpO2 : 97.0,
                };
                await RunAIPredictionAsync(request);
            }
            catch { }
        }

        // Apelează serviciul AI de predicție și actualizează starea UI (loading/rezultat/eroare).
        private async Task RunAIPredictionAsync(AIPredictionRequestDTO request)
        {
            try
            {
                AIPredictionLoading = true;
                await InvokeAsync(StateHasChanged);
                AIPrediction = await AIPredictionService.GetPredictionAsync(request);
            }
            catch
            {
                AIPrediction = null;
            }
            finally
            {
                AIPredictionLoading = false;
                await InvokeAsync(StateHasChanged);
            }
        }

        // Încarcă predicțiile de tendință (evoluție probabilă a valorilor vitale) pe baza istoricului
        // de monitorizare al persoanei, de la endpoint-ul de monitoring.
        private async Task LoadTrendPredictionsAsync(Guid monitoredId)
        {
            try
            {
                TrendPredictionsLoading = true;
                await InvokeAsync(StateHasChanged);

                var response = await HttpClient.GetAsync($"api/monitoring/{monitoredId}/predictions");
                if (response.IsSuccessStatusCode)
                    TrendPredictions = await response.Content.ReadFromJsonAsync<LifeAlertPlus.Shared.DTOs.Responses.Monitoring.TrendPredictionResponseDTO>();
                else
                    TrendPredictions = null;
            }
            catch
            {
                TrendPredictions = null;
            }
            finally
            {
                TrendPredictionsLoading = false;
                await InvokeAsync(StateHasChanged);
            }
        }

        // Încarcă profilul de activitate al persoanei (pattern-uri de mișcare/odihnă derivate din date).
        private async Task LoadActivityProfileAsync(Guid monitoredId)
        {
            try
            {
                ActivityProfileLoading = true;
                await InvokeAsync(StateHasChanged);

                var response = await HttpClient.GetAsync($"api/activityprofile/{monitoredId}");
                ActivityProfile = response.IsSuccessStatusCode
                    ? await response.Content.ReadFromJsonAsync<ActivityProfileResponseDTO>()
                    : null;
            }
            catch
            {
                ActivityProfile = null;
            }
            finally
            {
                ActivityProfileLoading = false;
                await InvokeAsync(StateHasChanged);
            }
        }

        // ── Conditions ────────────────────────────────────────────
        // Secțiune dedicată afecțiunilor medicale (comorbidități) asociate persoanei monitorizate.
        // Afecțiunile influențează pragurile vitale efective (vezi ComputeStatus/CalculateConditionThresholds).

        private List<string> _conditions = new();
        private List<string> _editConditions = new();
        private bool _conditionsLoading;
        private bool _showConditionsModal;
        private bool _isSavingConditions;

        // Definește o grupare vizuală de afecțiuni (categorie, iconiță, culoare) pentru UI.
        private record ConditionGroupDef(string CategoryKey, string Icon, string ColorClass, List<string> Keys);

        // Gruparea afecțiunilor disponibile pe categorii medicale, pentru afișare organizată în modalul de editare.
        private static readonly List<ConditionGroupDef> ConditionGroups = new()
        {
            new("conditions.cardio",       "❤️",  "cardio",      new() { "hypertension", "arrhythmia", "heart_failure", "mi_risk" }),
            new("conditions.respiratory",  "🫁",  "respiratory", new() { "asthma", "copd" }),
            new("conditions.neuro",        "🧠",  "neuro",       new() { "parkinson", "epilepsy" }),
            new("conditions.metabolic",    "🔬",  "metabolic",   new() { "diabetes" }),
        };

        // Încarcă afecțiunile medicale salvate ale persoanei și recalculează statusul vital
        // (deoarece pragurile efective depind de afecțiunile cunoscute).
        private async Task LoadConditionsAsync(Guid monitoredId)
        {
            _conditionsLoading = true;
            StateHasChanged();
            try
            {
                var response = await HttpClient.GetAsync($"api/monitoredcondition/{monitoredId}");
                _conditions = response.IsSuccessStatusCode
                    ? (await response.Content.ReadFromJsonAsync<List<string>>()) ?? new()
                    : new();
            }
            catch { _conditions = new(); }
            finally
            {
                _conditionsLoading = false;
                // Re-compute status now that conditions are known (unless person is offline)
                if (Person != null && Person.Status != "Offline")
                {
                    Person.Status = ComputeStatus(
                        Person.HeartRate, Person.SpO2, Person.Temperature,
                        _espData?.IsFall == true,
                        Person.MinHeartRate, Person.MaxHeartRate,
                        Person.MinTemperature, Person.MaxTemperature,
                        Person.MinSpO2,
                        _conditions);
                }
                StateHasChanged();
            }
        }

        // Calculează statusul vital al persoanei: "Critical", "Warning" sau "OK".
        // Logica este oglinda client-side a aceleiași evaluări făcută pe server: pragurile de bază
        // (min/max HR, Temp, SpO2) sunt mai întâi relaxate ("lărgite") pe baza afecțiunilor cunoscute
        // ale pacientului (ex. un cardiac poate avea un puls maxim normal mai mare), iar apoi se
        // verifică abaterile față de pragurile EFECTIVE (ajustate), nu față de cele brute.
        private string ComputeStatus(
            int hr, int spo2, double temp, bool isFall,
            int minHr, int maxHr, double minTemp, double maxTemp, int minSpo2,
            IEnumerable<string>? conditions = null)
        {
            int effMinHr = minHr, effMaxHr = maxHr;
            double effMinTemp = minTemp, effMaxTemp = maxTemp;
            int effMinSpo2 = minSpo2;

            // Pentru fiecare afecțiune cunoscută, extinde (niciodată nu restrânge) intervalul normal —
            // se păstrează cel mai permisiv prag dintre toate afecțiunile combinate.
            foreach (var cond in conditions ?? Enumerable.Empty<string>())
            {
                if (_conditionProfiles.TryGetValue(cond, out var p))
                {
                    if (p.MinHr   < effMinHr)   effMinHr   = p.MinHr;
                    if (p.MaxHr   > effMaxHr)   effMaxHr   = p.MaxHr;
                    if (p.MinTemp < effMinTemp) effMinTemp = p.MinTemp;
                    if (p.MaxTemp > effMaxTemp) effMaxTemp = p.MaxTemp;
                    if (p.MinSpO2 < effMinSpo2) effMinSpo2 = p.MinSpO2;
                }
            }

            // Prag "Critical": cădere detectată de senzor, SAU abateri severe (HR cu o marjă suplimentară
            // de 10 bpm sub minim, SpO2 sub 90% — prag medical general de hipoxemie severă —, ori
            // temperatură cu ±0.5°C peste limitele efective). Aceste marje suplimentare reduc
            // falsele alarme critice pentru valori ușor peste prag.
            if (isFall ||
                (hr   > 0 && (hr   > effMaxHr            || hr   < effMinHr   - 10)) ||
                (spo2 > 0 &&  spo2 < 90)                                              ||
                (temp > 0 && (temp > effMaxTemp + 0.5     || temp < effMinTemp - 0.5)))
                return "Critical";

            // Prag "Warning": valori care se apropie de limită (HR la 10 bpm sub maxim, sau sub minim;
            // SpO2/Temp ușor în afara intervalului efectiv) — semnal de atenționare fără a fi încă critic.
            if ((hr   > 0 && (hr   > effMaxHr - 10        || hr   < effMinHr))       ||
                (spo2 > 0 &&  spo2 < effMinSpo2)                                      ||
                (temp > 0 && (temp > effMaxTemp            || temp < effMinTemp)))
                return "Warning";

            return "OK";
        }

        // Deschide modalul de editare a afecțiunilor, pornind de la lista curentă salvată,
        // și recalculează imediat previzualizarea pragurilor vitale ajustate.
        private void OpenConditionsModal()
        {
            _editConditions = new List<string>(_conditions);
            CalculateConditionThresholds();
            _showConditionsModal = true;
        }

        private void CloseConditionsModal() => _showConditionsModal = false;

        // Adaugă/elimină o afecțiune din lista editată și recalculează imediat previzualizarea pragurilor.
        private void ToggleCondition(string key, bool isChecked)
        {
            if (isChecked && !_editConditions.Contains(key))
                _editConditions.Add(key);
            else if (!isChecked)
                _editConditions.Remove(key);
            CalculateConditionThresholds();
        }

        // Client-side mirror of ConditionThresholdAdjuster.Calculate
        // Praguri vitale specifice fiecărei afecțiuni — trebuie să rămână sincronizate cu logica
        // echivalentă de pe server (ConditionThresholdAdjuster), altfel previzualizarea din UI
        // ar putea diferi de pragurile efectiv aplicate la evaluarea alertelor.
        private static readonly Dictionary<string, (int MinHr, int MaxHr, double MinTemp, double MaxTemp, int MinSpO2, int MaxSpO2)> _conditionProfiles = new()
        {
            ["heart_failure"] = (55, 110, 36.0, 37.5, 92, 100),
            ["arrhythmia"]    = (40, 130, 36.0, 37.5, 95, 100),
            ["copd"]          = (60, 110, 36.0, 37.5, 88, 100),
            ["asthma"]        = (60, 110, 36.0, 37.5, 90, 100),
            ["hypertension"]  = (60, 100, 36.0, 37.5, 95, 100),
            ["diabetes"]      = (60, 100, 36.0, 37.2, 95, 100),
            ["parkinson"]     = (55, 105, 36.0, 37.5, 95, 100),
            ["mi_risk"]       = (60, 100, 36.0, 37.5, 93, 100),
            ["epilepsy"]      = (60, 120, 36.0, 37.5, 95, 100),
        };

        // Recalculează pragurile vitale "previzualizate" (afișate în modalul de afecțiuni) pe baza
        // afecțiunilor selectate momentan în editor — pornind de la pragurile implicite și extinzându-le
        // cu cel mai permisiv prag dintre toate afecțiunile bifate (aceeași regulă ca în ComputeStatus).
        private void CalculateConditionThresholds()
        {
            int minHr = 60; int maxHr = 100;
            double minTemp = 36.0; double maxTemp = 37.5;
            int minSpO2 = 95; int maxSpO2 = 100;

            foreach (var key in _editConditions)
            {
                if (!_conditionProfiles.TryGetValue(key, out var p)) continue;
                if (p.MinHr   < minHr)   minHr   = p.MinHr;
                if (p.MaxHr   > maxHr)   maxHr   = p.MaxHr;
                if (p.MinTemp < minTemp) minTemp = p.MinTemp;
                if (p.MaxTemp > maxTemp) maxTemp = p.MaxTemp;
                if (p.MinSpO2 < minSpO2) minSpO2 = p.MinSpO2;
            }

            _previewMinHr   = minHr;
            _previewMaxHr   = maxHr;
            _previewMinTemp = minTemp;
            _previewMaxTemp = maxTemp;
            _previewMinSpO2 = minSpO2;
            _previewMaxSpO2 = maxSpO2;
        }

        // Salvează lista de afecțiuni editată pe server; serverul recalculează și el pragurile vitale
        // ajustate (sursa de adevăr), iar aceste praguri sunt sincronizate înapoi în formularul de editare.
        // La final, datele persoanei și predicția AI sunt reîncărcate pentru a reflecta noile praguri.
        // Flux: HTTP PUT api/monitoredcondition/{PersonId} ["hypertension", "copd", ...]
        //   → MonitoredConditionController.Replace()
        //   → IMonitoredConditionRepository.ReplaceAllAsync() → DB (replace-all)
        //   → ConditionThresholdAdjuster.Calculate() → praguri noi salvate pe Monitored
        //   → ConditionRuleEngine.InvalidateCache() + AlertMonitorService.InvalidateThresholdCache()
        //   → returnează ConditionThresholdResponseDTO cu noile praguri Min/Max per metric
        private async Task SaveConditionsAsync()
        {
            _isSavingConditions = true;
            StateHasChanged();
            try
            {
                var response = await HttpClient.PutAsJsonAsync($"api/monitoredcondition/{PersonId}", _editConditions);
                if (response.IsSuccessStatusCode)
                {
                    _conditions = new List<string>(_editConditions);
                    _showConditionsModal = false;

                    // Read auto-adjusted thresholds from response and sync edit modal fields
                    try
                    {
                        var thresholds = await response.Content.ReadFromJsonAsync<ConditionThresholdResponseDTO>();
                        if (thresholds != null)
                        {
                            _editMinHr   = thresholds.MinHeartRate;
                            _editMaxHr   = thresholds.MaxHeartRate;
                            _editMinTemp = thresholds.MinTemperature;
                            _editMaxTemp = thresholds.MaxTemperature;
                            _editMinSpO2 = thresholds.MinSpO2;
                            _editMaxSpO2 = thresholds.MaxSpO2;
                        }
                    }
                    catch { /* threshold sync is best-effort */ }

                    // Reload person data so vitals/status use updated thresholds
                    await LoadPersonDataAsync();

                    // Re-run AI with updated thresholds: live ESP first, last measurement as fallback
                    // Predicția AI trebuie reluată pentru că pragurile noi pot schimba interpretarea riscului.
                    try
                    {
                        var espData = await MonitoredApiClient.GetEspDataAsync(Person?.DeviceSerial ?? "");
                        if (espData?.IsAvailable == true)
                            _ = LoadAIPredictionAsync(espData);
                        else
                            _ = LoadAIPredictionFromLastMeasurementAsync();
                    }
                    catch { _ = LoadAIPredictionFromLastMeasurementAsync(); }
                }
            }
            catch { }
            finally
            {
                _isSavingConditions = false;
                StateHasChanged();
            }
        }

        // Încarcă notițele medicului asociate persoanei monitorizate (istoric observații clinice).
        private async Task LoadDoctorNotesAsync(Guid monitoredId)
        {
            _doctorNotesLoading = true;
            StateHasChanged();
            try
            {
                var response = await HttpClient.GetAsync($"api/monitored/{monitoredId}/notes");
                _doctorNotes = response.IsSuccessStatusCode
                    ? (await response.Content.ReadFromJsonAsync<List<DoctorNoteDTO>>()) ?? new()
                    : new();
            }
            catch { _doctorNotes = new(); }
            finally
            {
                _doctorNotesLoading = false;
                StateHasChanged();
            }
        }

        private void OpenDoctorNotesModal()
        {
            _doctorNoteContent = string.Empty;
            _showDoctorNotesModal = true;
        }

        private void CloseDoctorNotesModal() => _showDoctorNotesModal = false;

        // Trimite o notiță nouă a medicului către server și reîncarcă lista actualizată.
        // Flux: HTTP POST api/monitored/{PersonId}/notes { Content: "..." }
        //   → DoctorNoteController.SaveNote()
        //   → verifică că utilizatorul e medic cu invitație acceptată (nu îngrijitor/admin)
        //   → UPSERT în DoctorNotes (un medic = o notiță per pacient)
        //   → notifică toți îngrijitorii prin push + email
        //   → la succes: LoadDoctorNotesAsync() → reîncarcă lista
        private async Task SaveDoctorNoteAsync()
        {
            if (string.IsNullOrWhiteSpace(_doctorNoteContent)) return;
            _isSavingDoctorNote = true;
            StateHasChanged();
            try
            {
                var response = await HttpClient.PostAsJsonAsync($"api/monitored/{PersonId}/notes", new { Content = _doctorNoteContent.Trim() });
                if (response.IsSuccessStatusCode)
                {
                    _doctorNoteContent = string.Empty;
                    _showDoctorNotesModal = false;
                    await LoadDoctorNotesAsync(PersonId);
                }
            }
            catch { }
            finally
            {
                _isSavingDoctorNote = false;
                StateHasChanged();
            }
        }

        // DTO local pentru pragurile vitale ajustate automat, returnate de server după salvarea afecțiunilor.
        private sealed class ConditionThresholdResponseDTO
        {
            public int? MinHeartRate   { get; set; }
            public int? MaxHeartRate   { get; set; }
            public double? MinTemperature { get; set; }
            public double? MaxTemperature { get; set; }
            public int? MinSpO2        { get; set; }
            public int? MaxSpO2        { get; set; }
        }

        // Mapează eticheta unui interval orar de activitate la o clasă CSS pentru colorarea în UI.
        private static string GetActivitySlotClass(string label) => label switch
        {
            "Somn" => "slot-sleep",
            "Activ" => "slot-active",
            "Moderat activ" => "slot-moderate",
            "Inactiv / Odihnă" => "slot-rest",
            _ => "slot-nodata"
        };

        // Construiește textul tooltip pentru un interval orar din profilul de activitate
        // (puls mediu, rată de mișcare, probabilitate de somn); necesită minim 10 puncte de date.
        private string GetSlotTooltip(HourlyProfileDTO? slot, int hour)
        {
            if (slot == null || slot.DataPoints < 10)
                return $"{hour:00}:00 – {T("selected.activityNoData")}";
            return $"{hour:00}:00 | {slot.Label} | {T("selected.slotAvgPulse")}: {slot.AveragePulse:F0} bpm | {T("selected.slotMovement")}: {slot.MovementRate:P0} | {T("selected.activitySleep")}: {slot.SleepProbability:P0}";
        }

        // Variante filtrate ale istoricului, excluzând punctele fără date reale (pentru afișări care
        // nu trebuie să interpoleze/conecteze vizual segmentele lipsă).
        private List<ChartDataPoint> HeartRateHistoryFiltered =>
            HeartRateHistory.Where(d => d.HasData).ToList();
        private List<ChartDataPoint> TemperatureHistoryFiltered =>
            TemperatureHistory.Where(d => d.HasData).ToList();

        // Încarcă măsurătorile din API și populează datele graficelor (zilnic sau săptămânal),
        // apoi recalculează proiecțiile de puncte SVG și tooltip-urile.
        private async Task LoadChartDataAsync()
        {
            try
            {
                // Fereastra săptămânală are nevoie de mult mai multe măsurători brute (agregate pe zi),
                // de aceea se cere un fetch size mult mai mare decât pentru vizualizarea zilnică.
                int fetchSize = CurrentChartView == ChartViewMode.Weekly ? 10000 : 1000;
                var measurements = await MeasurementApiClient.GetMeasurementsByMonitoredIdAsync(PersonId, 1, fetchSize);
                if (measurements == null || !measurements.Any())
                {
                    LoadEmptyChartData();
                    return;
                }

                var measurementsList = measurements.ToList();

                if (CurrentChartView == ChartViewMode.Daily)
                {
                    LoadDailyChartData(measurementsList);
                }
                else
                {
                    LoadWeeklyChartData(measurementsList);
                }

                HeartRatePoints   = ComputePointsWithRange(HeartRateHistory,   40,  140);
                TemperaturePoints = ComputePointsWithRange(TemperatureHistory,  35,   39);
                SpO2Points        = ComputePointsWithRange(SpO2History,          85,  100);
                HrTooltipData     = ComputeTooltipData(HeartRateHistory,   40,  120);
                TempTooltipData   = ComputeTooltipData(TemperatureHistory,  35,   39);
                SpO2TooltipData   = ComputeTooltipData(SpO2History,          85,  100);

                // Ensure UI updates immediately after data is loaded so points appear
                await InvokeAsync(StateHasChanged);
            }
            catch
            {
                LoadEmptyChartData();
            }
        }

        // Pregătește datele graficului pentru vizualizarea pe o singură zi (ziua curentă + _dayOffset).
        // Fiecare măsurătoare individuală devine un punct pe grafic (nu se agregă), poziționat pe axa X
        // proporțional cu ora din zi (XFraction), permițând zoom și tooltip per-măsurătoare.
        private void LoadDailyChartData(List<MeasurementResponseDTO> measurements)
        {
            var targetDay = DateTime.Now.Date.AddDays(_dayOffset);

            // Set day label
            if (_dayOffset == 0)
                _chartDayLabel = targetDay.ToString("dddd, dd MMM yyyy");
            else
                _chartDayLabel = targetDay.ToString("dddd, dd MMM yyyy");

            // Cache all days with data so navigation can jump directly to them
            _daysWithData = measurements
                .Select(m => m.CreatedAt.ToLocalTime().Date)
                .ToHashSet();

            // Enable back-arrow whenever any day before targetDay has data
            _hasPrevDayData = _daysWithData.Any(d => d < targetDay);

            var todayMs = measurements
                .Where(m => m.CreatedAt.ToLocalTime().Date == targetDay)
                .OrderBy(m => m.CreatedAt)
                .ToList();

            if (!todayMs.Any()) { LoadEmptyChartData(); return; }

            // One point per measurement — the wide, zoomable chart gives them enough room
            // to be readable as distinct dots, and each one is hover-tooltip addressable.
            // Sensor-failure readings (= 0) are excluded per metric.
            // (citirile cu valoare 0 indică, de regulă, o eroare/deconectare a senzorului, nu o valoare reală)
            HeartRateHistory = todayMs
                .Where(m => m.Pulse > 0)
                .Select(m =>
                {
                    var t = m.CreatedAt.ToLocalTime();
                    return new ChartDataPoint
                    {
                        Day = t.ToString("HH:mm"),
                        ActualValue = m.Pulse,
                        HasData = true,
                        XFraction = t.TimeOfDay.TotalHours / 24.0
                    };
                })
                .OrderBy(p => p.XFraction)
                .ToList();

            TemperatureHistory = todayMs
                .Where(m => m.Temperature > 0)
                .Select(m =>
                {
                    var t = m.CreatedAt.ToLocalTime();
                    return new ChartDataPoint
                    {
                        Day = t.ToString("HH:mm"),
                        ActualValue = m.Temperature,
                        HasData = true,
                        XFraction = t.TimeOfDay.TotalHours / 24.0
                    };
                })
                .OrderBy(p => p.XFraction)
                .ToList();

            SpO2History = todayMs
                .Where(m => m.SpO2 > 0)
                .Select(m =>
                {
                    var t = m.CreatedAt.ToLocalTime();
                    return new ChartDataPoint
                    {
                        Day = t.ToString("HH:mm"),
                        ActualValue = m.SpO2,
                        HasData = true,
                        XFraction = t.TimeOfDay.TotalHours / 24.0
                    };
                })
                .OrderBy(p => p.XFraction)
                .ToList();
        }

        // Eșantionează uniform o listă, alegând `count` elemente distribuite egal de-a lungul ei
        // (folosit probabil pentru a reduce densitatea punctelor afișate pe grafic).
        private static List<T> SampleEvenly<T>(List<T> source, int count)
        {
            var result = new List<T>(count);
            double step = (double)(source.Count - 1) / (count - 1);
            for (int i = 0; i < count; i++)
                result.Add(source[(int)Math.Round(i * step)]);
            return result;
        }

        // Aplică o medie mobilă (sliding window) pentru a netezi o serie de valori, reducând zgomotul
        // vizual din grafic fără a modifica datele brute originale.
        private static List<double> SmoothValues(List<double> values, int window)
        {
            var result = new List<double>(values.Count);
            int half = window / 2;
            for (int i = 0; i < values.Count; i++)
            {
                int from = Math.Max(0, i - half);
                int to   = Math.Min(values.Count - 1, i + half);
                double sum = 0;
                for (int j = from; j <= to; j++) sum += values[j];
                result.Add(sum / (to - from + 1));
            }
            return result;
        }

        // Pregătește datele graficului pentru vizualizarea săptămânală: agregă măsurătorile pe zi
        // (medie zilnică) pentru cele 7 zile ale săptămânii selectate (curentă + _weekOffset).
        private void LoadWeeklyChartData(List<MeasurementResponseDTO> measurements)
        {
            var today = DateTime.Now.Date;

            // Find the start of the current week based on user's preferred first day
            int diff = ((int)today.DayOfWeek - (int)_firstDayOfWeek + 7) % 7;
            var currentWeekStart = today.AddDays(-diff);
            var weekStart = currentWeekStart.AddDays(_weekOffset * 7);
            var days = Enumerable.Range(0, 7).Select(i => weekStart.AddDays(i)).ToList();

            // Set week label
            _chartWeekLabel = $"{weekStart:dd MMM} - {days[6]:dd MMM yyyy}";

            // Check if previous week has data
            var prevWeekStart = weekStart.AddDays(-7);
            var prevWeekEnd = weekStart.AddDays(-1);
            _hasPrevWeekData = measurements.Any(m =>
            {
                var d = m.CreatedAt.ToLocalTime().Date;
                return d >= prevWeekStart && d <= prevWeekEnd;
            });

            var hrByDay = measurements
                .Where(m => m.CreatedAt.ToLocalTime().Date >= days[0] && m.CreatedAt.ToLocalTime().Date <= days[6])
                .GroupBy(m => m.CreatedAt.ToLocalTime().Date)
                .ToDictionary(g => g.Key, g => g.Average(m => (double)m.Pulse));

            var tempByDay = measurements
                .Where(m => m.CreatedAt.ToLocalTime().Date >= days[0] && m.CreatedAt.ToLocalTime().Date <= days[6])
                .GroupBy(m => m.CreatedAt.ToLocalTime().Date)
                .ToDictionary(g => g.Key, g => g.Average(m => m.Temperature));

            HeartRateHistory = days.Select(day => new ChartDataPoint
            {
                Day = day.ToString("ddd", System.Globalization.CultureInfo.InvariantCulture),
                ActualValue = hrByDay.TryGetValue(day, out var v) ? v : 0,
                HasData = hrByDay.ContainsKey(day)
            }).ToList();

            TemperatureHistory = days.Select(day => new ChartDataPoint
            {
                Day = day.ToString("ddd", System.Globalization.CultureInfo.InvariantCulture),
                ActualValue = tempByDay.TryGetValue(day, out var v) ? v : 0,
                HasData = tempByDay.ContainsKey(day)
            }).ToList();

            var spo2ByDay = measurements
                .Where(m => m.CreatedAt.ToLocalTime().Date >= days[0] && m.CreatedAt.ToLocalTime().Date <= days[6] && m.SpO2 > 0)
                .GroupBy(m => m.CreatedAt.ToLocalTime().Date)
                .ToDictionary(g => g.Key, g => g.Average(m => m.SpO2));

            SpO2History = days.Select(day => new ChartDataPoint
            {
                Day = day.ToString("ddd", System.Globalization.CultureInfo.InvariantCulture),
                ActualValue = spo2ByDay.TryGetValue(day, out var v) ? v : 0,
                HasData = spo2ByDay.ContainsKey(day)
            }).ToList();
        }

        // Comută între vizualizarea zilnică și săptămânală a graficelor, resetând offset-urile
        // de navigare și forțând o reîncărcare completă a datelor.
        private async Task SwitchChartView(ChartViewMode mode)
        {
            CurrentChartView = mode;
            _weekOffset = 0;
            _dayOffset = 0;
            HeartRateHistory = new List<ChartDataPoint>();
            TemperatureHistory = new List<ChartDataPoint>();
            SpO2History = new List<ChartDataPoint>();
            HeartRatePoints = new List<(double X, double Y)>();
            TemperaturePoints = new List<(double X, double Y)>();
            SpO2Points = new List<(double X, double Y)>();
            HrTooltipData = new List<TooltipPoint>();
            TempTooltipData = new List<TooltipPoint>();
            SpO2TooltipData = new List<TooltipPoint>();
            _tooltipsInitialized = false;
            StateHasChanged();
            await Task.Delay(50);
            await LoadChartDataAsync();
            StateHasChanged();
            await InitTooltipsAsync();
        }

        // Navighează la săptămâna precedentă (doar dacă există date confirmate pentru ea).
        private async Task GoToPreviousWeek()
        {
            if (!_hasPrevWeekData) return;
            _weekOffset--;
            await ReloadChartAsync();
        }

        // Navighează la săptămâna următoare; blocat dacă suntem deja în săptămâna curentă (offset 0).
        private async Task GoToNextWeek()
        {
            if (_weekOffset >= 0) return;
            _weekOffset++;
            await ReloadChartAsync();
        }

        // Navighează la cea mai recentă zi anterioară care are date înregistrate (sare peste zilele goale).
        private async Task GoToPreviousDay()
        {
            if (!_hasPrevDayData) return;
            var currentDay = DateTime.Now.Date.AddDays(_dayOffset);
            // Jump to the most recent day BEFORE currentDay that has data
            for (int i = 1; i <= 365; i++)
            {
                var candidate = currentDay.AddDays(-i);
                if (_daysWithData.Contains(candidate))
                {
                    _dayOffset = (int)(candidate - DateTime.Now.Date).TotalDays;
                    await ReloadChartAsync();
                    return;
                }
            }
        }

        // Navighează la cea mai apropiată zi următoare cu date (fără a trece de ziua curentă).
        private async Task GoToNextDay()
        {
            if (_dayOffset >= 0) return;
            var currentDay = DateTime.Now.Date.AddDays(_dayOffset);
            // Jump to the nearest day AFTER currentDay that has data (up to today)
            for (int i = 1; i <= 365; i++)
            {
                var candidate = currentDay.AddDays(i);
                if (candidate > DateTime.Now.Date) break;
                if (_daysWithData.Contains(candidate))
                {
                    _dayOffset = (int)(candidate - DateTime.Now.Date).TotalDays;
                    await ReloadChartAsync();
                    return;
                }
            }
        }

        // Golește temporar datele graficelor (pentru un mic efect de tranziție vizuală), apoi
        // reîncarcă datele pentru noul interval/offset selectat și reinițializează tooltip-urile.
        private async Task ReloadChartAsync()
        {
            HeartRateHistory = new List<ChartDataPoint>();
            TemperatureHistory = new List<ChartDataPoint>();
            SpO2History = new List<ChartDataPoint>();
            HeartRatePoints = new List<(double X, double Y)>();
            TemperaturePoints = new List<(double X, double Y)>();
            SpO2Points = new List<(double X, double Y)>();
            HrTooltipData = new List<TooltipPoint>();
            TempTooltipData = new List<TooltipPoint>();
            SpO2TooltipData = new List<TooltipPoint>();
            _tooltipsInitialized = false;
            StateHasChanged();
            await Task.Delay(50);
            await LoadChartDataAsync();
            StateHasChanged();
            await InitTooltipsAsync();
        }

        // Variantă care folosește auto-scalare (min/max calculate din date) în loc de un interval fix.
        private List<(double X, double Y)> ComputePoints(List<ChartDataPoint> data)
        {
            return ComputePointsWithRange(data, 0, 0);
        }

        // Proiectează punctele de date pe coordonate SVG (X, Y) în interiorul viewBox-ului graficului.
        // Dacă fixedMin/fixedMax sunt 0/0, scala verticală se calculează automat din valorile reale
        // (cu o marjă de 20%); altfel se folosește intervalul fix dat (ex. limitele fiziologice normale).
        private List<(double X, double Y)> ComputePointsWithRange(
            List<ChartDataPoint> data, double fixedMin, double fixedMax)
        {
            if (data == null || data.Count == 0) return new();

            const double paddingLeft = 90;
            const double paddingRight = 15;
            const double paddingTop = 15;
            var usableWidth = ChartViewBoxWidth - paddingLeft - paddingRight;  // grows with zoom
            var usableHeight = 145.0;  // 200 - 15 - 40

            double minVal, maxVal;
            if (fixedMin == 0 && fixedMax == 0)
            {
                var vals = data.Where(d => d.HasData).Select(d => d.ActualValue).ToList();
                if (!vals.Any()) return new();
                minVal = vals.Min();
                maxVal = vals.Max();
                var rng = maxVal - minVal;
                if (rng < 0.001) { minVal -= 1; maxVal += 1; }
                else { minVal -= rng * 0.2; maxVal += rng * 0.2; }
            }
            else
            {
                minVal = fixedMin;
                maxVal = fixedMax;
            }

            var range = maxVal - minVal;
            if (range < 0.001) range = 10;

            int n = data.Count;
            var pts = data
                .Select((d, i) => (
                    HasData: d.HasData,
                    X: paddingLeft + (d.XFraction >= 0
                        ? d.XFraction * usableWidth
                        : (n <= 1 ? usableWidth / 2.0 : (double)i / (n - 1) * usableWidth)),
                    Y: paddingTop + usableHeight * (1.0 - Math.Clamp((d.ActualValue - minVal) / range, 0.0, 1.0))
                ))
                .Where(p => p.HasData)
                .OrderBy(p => p.X)
                .Select(p => (X: p.X, Y: p.Y))
                .ToList();

            // Spread points that are very close on X so circles don't overlap
            // (separă vizual punctele apropiate pe orizontală, ca cercurile de pe grafic să nu se suprapună)
            var minX = paddingLeft;
            var maxX = paddingLeft + usableWidth;
            double spacing = CurrentChartView == ChartViewMode.Daily ? 12.0 : 8.0;
            return SpreadCloseXs(pts, minX, maxX, spacing);
        }

        // Generează etichetele axei X: ore fixe (la fiecare 4h) pentru vizualizarea zilnică,
        // respectiv numele zilelor săptămânii pentru vizualizarea săptămânală.
        private List<(string Label, double X)> GetXAxisLabels()
        {
            const double paddingLeft = 90;
            const double paddingRight = 15;
            var usableWidth = ChartViewBoxWidth - paddingLeft - paddingRight;

            if (CurrentChartView == ChartViewMode.Daily)
            {
                // Fixed hour labels regardless of data — every 4h
                return new[] { 0, 4, 8, 12, 16, 20, 23 }
                    .Select(h => (Label: $"{h:00}:00", X: paddingLeft + (h / 23.0) * usableWidth))
                    .ToList();
            }
            else
            {
                var data = HeartRateHistory;
                if (data == null || data.Count == 0) return new();
                int n = data.Count;
                return data
                    .Select((d, i) => (
                        Label: d.Day,
                        X: paddingLeft + (n <= 1 ? usableWidth / 2.0 : (double)i / (n - 1) * usableWidth)
                    ))
                    .ToList();
            }
        }
// Formatează un număr cu 2 zecimale, cultură invariantă, pentru construirea atributelor SVG "d"/"points".
private static string F(double v) => v.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

        // Generează atributul SVG "path" pentru zona umplută de sub curba graficului (aria de sub linie).
        private string GenerateAreaPath(List<(double X, double Y)> pts, double baseline = 160)
        {
            if (pts == null || pts.Count == 0) return "";
            if (pts.Count == 1)
            {
                // Small filled rectangle under the single point so the area is visible
                var x1 = pts[0].X;
                var y1 = pts[0].Y;
                var x2 = x1 + 1.0; // tiny width
                return $"M {F(x1)} {F(y1)} L {F(x2)} {F(y1)} L {F(x2)} {F(baseline)} L {F(x1)} {F(baseline)} Z";
            }

            var linePath = GenerateSmoothPath(pts);
            if (string.IsNullOrEmpty(linePath)) return "";
            return $"{linePath} L {F(pts[pts.Count - 1].X)} {F(baseline)} L {F(pts[0].X)} {F(baseline)} Z";
        }

        /// <summary>
        /// Monotone cubic Hermite interpolation (Fritsch-Carlson).
        /// Guarantees no overshooting between data points — curves never loop or twist.
        /// </summary>
        private string GenerateSmoothPath(List<(double X, double Y)> pts)
        {
            if (pts == null || pts.Count == 0) return "";
            if (pts.Count == 1)
            {
                // Draw a very short segment so a visible stroke appears for a single point
                return $"M {F(pts[0].X)} {F(pts[0].Y)} L {F(pts[0].X + 1.0)} {F(pts[0].Y)}";
            }

            int n = pts.Count;

            // For exactly 2 points, just draw a line
            if (n == 2)
                return $"M {F(pts[0].X)} {F(pts[0].Y)} L {F(pts[1].X)} {F(pts[1].Y)}";

            // 1. Compute segment deltas and slopes
            var dx = new double[n - 1];
            var dy = new double[n - 1];
            var slopes = new double[n - 1];
            for (int i = 0; i < n - 1; i++)
            {
                dx[i] = pts[i + 1].X - pts[i].X;
                dy[i] = pts[i + 1].Y - pts[i].Y;
                slopes[i] = dx[i] < 1e-10 ? 0 : dy[i] / dx[i];
            }

            // 2. Compute initial tangents
            var m = new double[n];
            m[0] = slopes[0];
            m[n - 1] = slopes[n - 2];
            for (int i = 1; i < n - 1; i++)
            {
                if (slopes[i - 1] * slopes[i] <= 0)
                    m[i] = 0; // local extremum — flat tangent
                else
                    m[i] = (slopes[i - 1] + slopes[i]) / 2.0;
            }
            // 3. Fritsch-Carlson monotonicity correction
            for (int i = 0; i < n - 1; i++)
            {
                if (Math.Abs(slopes[i]) < 1e-10)
                {
                    m[i] = 0;
                    m[i + 1] = 0;
                }
                else
                {
                    double alpha = m[i] / slopes[i];
                    double beta = m[i + 1] / slopes[i];
                    double mag = alpha * alpha + beta * beta;
                    if (mag > 9)
                    {
                        double tau = 3.0 / Math.Sqrt(mag);
                        m[i] = tau * alpha * slopes[i];
                        m[i + 1] = tau * beta * slopes[i];
                    }
                }
            }

            // 4. Build SVG path with cubic bezier segments
            var path = new System.Text.StringBuilder();
            path.Append($"M {F(pts[0].X)} {F(pts[0].Y)}");

            for (int i = 0; i < n - 1; i++)
            {
                double seg = dx[i] / 3.0;
                double cp1x = pts[i].X + seg;
                double cp1y = pts[i].Y + m[i] * seg;
                double cp2x = pts[i + 1].X - seg;
                double cp2y = pts[i + 1].Y - m[i + 1] * seg;

                path.Append($" C {F(cp1x)} {F(cp1y)}, {F(cp2x)} {F(cp2y)}, {F(pts[i + 1].X)} {F(pts[i + 1].Y)}");
            }

            return path.ToString();
        }

        // Resetează toate seriile de date și punctele graficelor la liste vide (ex. când nu există
        // măsurători pentru intervalul selectat).
        private void LoadEmptyChartData()
        {
            HeartRateHistory   = new List<ChartDataPoint>();
            TemperatureHistory = new List<ChartDataPoint>();
            SpO2History        = new List<ChartDataPoint>();
            HeartRatePoints    = new List<(double X, double Y)>();
            TemperaturePoints  = new List<(double X, double Y)>();
            SpO2Points         = new List<(double X, double Y)>();
            HrTooltipData      = new List<TooltipPoint>();
            TempTooltipData    = new List<TooltipPoint>();
            SpO2TooltipData    = new List<TooltipPoint>();
        }

        // Spread close X positions so plotted circles don't visually overlap
        // Grupează punctele ale căror poziții X sunt mai apropiate decât `spacing` și le redistribuie
        // simetric în jurul centrului grupului, respectând limitele minX/maxX ale zonei de desenare.
        private static List<(double X, double Y)> SpreadCloseXs(List<(double X, double Y)> pts, double minX, double maxX, double spacing = 6.0)
        {
            if (pts == null) return new();
            if (pts.Count <= 1) return pts;

            // pts expected sorted by X
            var result = pts.Select(p => (X: p.X, Y: p.Y)).ToList();
            int i = 0;
            while (i < result.Count)
            {
                int j = i + 1;
                // group points that are closer than spacing
                while (j < result.Count && (result[j].X - result[j - 1].X) <= spacing)
                    j++;

                int groupSize = j - i;
                if (groupSize > 1)
                {
                    // center of group
                    double center = 0;
                    for (int k = i; k < j; k++) center += result[k].X;
                    center /= groupSize;

                    double startOffset = -((groupSize - 1) / 2.0) * spacing;
                    var newXs = new double[groupSize];
                    for (int k = 0; k < groupSize; k++)
                        newXs[k] = center + startOffset + k * spacing;

                    // ensure within bounds
                    double leftMost = newXs[0];
                    double rightMost = newXs[groupSize - 1];
                    if (leftMost < minX)
                    {
                        double shift = minX - leftMost;
                        for (int k = 0; k < groupSize; k++) newXs[k] += shift;
                    }
                    if (rightMost > maxX)
                    {
                        double shift = maxX - rightMost;
                        for (int k = 0; k < groupSize; k++) newXs[k] += shift;
                    }

                    for (int k = 0; k < groupSize; k++)
                        result[i + k] = (newXs[k], result[i + k].Y);
                }

                i = j;
            }

            return result;
        }

        // Derivă lista de "alerte recente" direct din ultimele măsurători (HR în afara intervalului,
        // temperatură peste maxim, cădere detectată) — este o reconstrucție client-side a evenimentelor
        // de alertă, nu provine dintr-un endpoint dedicat de alerte.
        private async Task LoadRecentAlertsAsync()
        {
            try
            {
                var measurements = await MeasurementApiClient.GetMeasurementsByMonitoredIdAsync(PersonId, 1, 50);
                if (measurements == null || !measurements.Any())
                {
                    RecentAlerts = new List<Alert>();
                    return;
                }

                var alerts = new List<Alert>();

                int alertMinHr  = Person?.MinHeartRate  ?? 60;
                int alertMaxHr  = Person?.MaxHeartRate  ?? 100;
                double alertMaxTemp = Person?.MaxTemperature ?? 37.5;

                // Se analizează doar cele mai recente 10 măsurători (din cele 50 încărcate) pentru performanță.
                foreach (var m in measurements.Take(10))
                {
                    if (m.Pulse > alertMaxHr || m.Pulse < alertMinHr)
                    {
                        alerts.Add(new Alert
                        {
                            Severity = "Critical",
                            Title = m.Pulse > alertMaxHr ? "High Heart Rate" : "Low Heart Rate",
                            Description = $"Heart rate: {m.Pulse} bpm",
                            Time = GetTimeAgo(m.CreatedAt)
                        });
                    }

                    if (m.Temperature > alertMaxTemp)
                    {
                        alerts.Add(new Alert
                        {
                            Severity = "Warning",
                            Title = "High Temperature",
                            Description = $"Temperature: {m.Temperature:F1}°C",
                            Time = GetTimeAgo(m.CreatedAt)
                        });
                    }

                    if (m.IsFall)
                    {
                        alerts.Add(new Alert
                        {
                            Severity = "Critical",
                            Title = "Fall Detected",
                            Description = $"Fall event detected - {m.Activity}",
                            Time = GetTimeAgo(m.CreatedAt)
                        });
                    }
                }

                // Se afișează doar primele 5 alerte derivate, cele mai relevante/recente.
                RecentAlerts = alerts.Take(5).ToList();
            }
            catch
            {
                RecentAlerts = new List<Alert>();
            }
        }

        // Încarcă cele mai recente 4 măsurători pentru afișarea în lista de "activitate recentă".
        private async Task LoadRecentMeasurementsAsync()
        {
            try
            {
                var measurements = await MeasurementApiClient.GetMeasurementsByMonitoredIdAsync(PersonId, 1, 4);
                if (measurements == null || !measurements.Any())
                {
                    RecentMeasurements = new List<Measurement>();
                    return;
                }

                RecentMeasurements = measurements.Select(m => new Measurement
                {
                    Icon = "❤️",
                    Type = m.Name,
                    Value = $"{m.Pulse} bpm, {m.Temperature:F1}°C",
                    Time = GetTimeAgo(m.CreatedAt)
                }).ToList();
            }
            catch
            {
                RecentMeasurements = new List<Measurement>();
            }
        }

        // Formatează un timestamp relativ la data curentă, ajustând nivelul de detaliu (doar ora dacă
        // e azi, zi+lună+oră dacă e în acest an, altfel data completă), respectând limba selectată.
        private string GetTimeAgo(DateTime dateTime)
        {
            var local = dateTime.Kind == DateTimeKind.Utc ? dateTime.ToLocalTime() : dateTime;
            var today = DateTime.Now.Date;
            var culture = Lang.CurrentLanguage == "ro"
                ? new System.Globalization.CultureInfo("ro-RO")
                : System.Globalization.CultureInfo.InvariantCulture;
            if (local.Date == today)
                return local.ToString("HH:mm", culture);
            if (local.Year == today.Year)
                return local.ToString("dd MMM HH:mm", culture);
            return local.ToString("dd MMM yyyy HH:mm", culture);
        }

        // Extrage inițialele (prenume + nume) dintr-un nume complet, pentru afișarea unui avatar text.
        private string GetInitials(string name)
        {
            var parts = name.Split(' ');
            if (parts.Length >= 2)
                return $"{parts[0][0]}{parts[1][0]}".ToUpper();
            return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpper();
        }

        // Mapează codul de status intern ("critical"/"warning"/"ok"/"offline") la un text descriptiv pentru UI.
        private string GetStatusText(string status)
        {
            return status.ToLower() switch
            {
                "critical" => "Critical Alert",
                "warning" => "Needs Attention",
                "ok" => "Stable",
                "offline" => "Offline",
                _ => "Unknown"
            };
        }

        // Convertește preferința utilizatorului pentru prima zi a săptămânii (text) în enum DayOfWeek;
        // implicit luni, dacă valoarea e necunoscută/lipsă.
        private static DayOfWeek ParseFirstDayOfWeek(string? value)
        {
            return (value?.ToLowerInvariant()) switch
            {
                "monday" => DayOfWeek.Monday,
                "tuesday" => DayOfWeek.Tuesday,
                "wednesday" => DayOfWeek.Wednesday,
                "thursday" => DayOfWeek.Thursday,
                "friday" => DayOfWeek.Friday,
                "saturday" => DayOfWeek.Saturday,
                "sunday" => DayOfWeek.Sunday,
                _ => DayOfWeek.Monday
            };
        }

        // Inițializarea componentei: determină contextul de acces (medic vs. proprietar/îngrijitor),
        // încarcă preferințele utilizatorului (praguri implicite, prima zi a săptămânii), încarcă
        // datele inițiale ale paginii (persoană, grafice, măsurători, alerte), se abonează la
        // evenimentele live (push notifications, măsurători noi) și pornește timer-ul de auto-refresh.
        protected override async Task OnInitializedAsync()
        {
            _instance = this;
            // Detect if user is accessing as a doctor (via /doctor/patient route)
            try
            {
                var uri = new Uri(NavigationManager.Uri);
                _isDoctorUser = uri.AbsolutePath.Contains("/doctor/patient/");

                var qs = ParseQueryString(uri.Query);
                if (qs.TryGetValue("showEdit", out var sval))
                {
                    if (bool.TryParse(sval, out var parsed))
                        _showEditButton = parsed;
                    else
                        _showEditButton = !string.Equals(sval, "false", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { /* ignore parsing errors and keep default true */ }
            var claims = await TokenParserService.GetClaimsAsync();
            if (claims != null)
            {
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
                    _firstDayOfWeek = ParseFirstDayOfWeek(userProfile.FirstDayOfTheWeek);
                    if (userProfile.MinHeartRate > 0) _userMinHr = userProfile.MinHeartRate;
                    if (userProfile.MaxHeartRate > 0) _userMaxHr = userProfile.MaxHeartRate;
                    if (userProfile.MinTemperature > 0) _userMinTemp = userProfile.MinTemperature;
                    if (userProfile.MaxTemperature > 0) _userMaxTemp = userProfile.MaxTemperature;
                    if (userProfile.UpdateFrequency > 0) _userUpdateFrequency = userProfile.UpdateFrequency;
                }
            }
            else
            {
                UserFullName = "User";
            }

            await Task.WhenAll(LoadPersonDataAsync(), LoadChartDataAsync(), LoadRecentMeasurementsAsync());
            await LoadRecentAlertsAsync();

            PushService.OnNotificationReceived += OnPushNotificationReceived;
            MeasurementApiClient.OnMeasurementAdded += OnMeasurementAdded;

            _ = LoadPendingFeedbackAsync();

            // Start auto-refresh timer (uses user-configured update frequency)
            // Timer-ul de polling rulează la intervalul de actualizare configurat de utilizator,
            // ținând pagina sincronizată cu noile date de la device fără a necesita reîncărcare manuală.
            _refreshTimer = new System.Threading.Timer(_ => _ = RefreshDataAsync(), null, TimeSpan.FromSeconds(_userUpdateFrequency), TimeSpan.FromSeconds(_userUpdateFrequency));
        }

        // Reîmprospătează toate datele live ale paginii (persoană, grafice, măsurători, alerte, hartă,
        // predicții, notițe). Este punctul central apelat atât de timer-ul periodic, cât și de
        // evenimentele push/măsurători noi — de aceea are protecție împotriva execuțiilor concurente.
        private async Task RefreshDataAsync()
        {
            if (_disposed) return;

            // Drop overlapping refreshes: a single cascade already issues ~8 GETs.
            // Without this guard, push notifications + timer + measurement events
            // can stack and produce multiplied request bursts.
            // Garda Interlocked.Exchange asigură că doar un singur refresh rulează la un moment dat —
            // dacă unul e deja în curs, cel nou este abandonat (return) în loc să se suprapună.
            if (System.Threading.Interlocked.Exchange(ref _refreshInFlight, 1) == 1)
                return;
            // Throttling suplimentar: dacă ultimul refresh a avut loc foarte recent (sub 5s),
            // se renunță la acest refresh pentru a evita rafale de cereri API.
            if (DateTime.UtcNow - _lastRefreshUtc < MinRefreshInterval)
            {
                System.Threading.Interlocked.Exchange(ref _refreshInFlight, 0);
                return;
            }

            try
            {
                await InvokeAsync(async () =>
                {
                    await Task.WhenAll(LoadPersonDataAsync(), LoadChartDataAsync(), LoadRecentMeasurementsAsync());
                    await LoadRecentAlertsAsync();
                    // Reset map so it re-initializes with fresh GPS coordinates
                    _mapInitialized = false;
                    _ = LoadTrendPredictionsAsync(PersonId);
                    _ = LoadDoctorNotesAsync(PersonId);
                    StateHasChanged();
                    await InitTooltipsAsync();
                    await InitMapAsync();
                });
            }
            catch
            {
                // Ignore errors during auto-refresh
            }
            finally
            {
                _lastRefreshUtc = DateTime.UtcNow;
                System.Threading.Interlocked.Exchange(ref _refreshInFlight, 0);
            }
        }

        // Hook-uri JS interop după fiecare render: inițializează tooltip-urile graficelor, harta GPS
        // (idempotent — sigur de apelat repetat) și sincronizarea de scroll dintre grafice HR/Temp.
        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender || !_tooltipsInitialized)
            {
                await InitTooltipsAsync();
            }

            // Try map init on every render — Person data loads async so it may
            // not be available on firstRender; InitMapAsync is idempotent.
            await InitMapAsync();

            // Pair the two SVG scroll wrappers so HR and Temp pan together on the X axis.
            if (firstRender && !_scrollSyncInitialized)
            {
                try
                {
                    await JSRuntime.InvokeVoidAsync("chartSync.attach", $"selected-{PersonId}",
                        new object[] { _hrScrollRef, _tempScrollRef });
                    _scrollSyncInitialized = true;
                }
                catch
                {
                    // chartSync.js may not be loaded yet during prerender — retry on next render
                }
            }
        }

        // Inițializează harta Google Maps cu poziția GPS curentă a persoanei monitorizate
        // (no-op dacă harta e deja inițializată sau dacă nu există coordonate GPS valide).
        private async Task InitMapAsync()
        {
            if (_mapInitialized) return;
            if (Person == null) return;
            if (string.IsNullOrWhiteSpace(Person.GPS)) return;

            if (!TryParseGpsToLatLon(Person.GPS, out double lat, out double lon))
            {
                return;
            }

            try
            {
                await JSRuntime.InvokeVoidAsync("googleMapsInterop.initMapOnElement", _mapRef, lat, lon);
                _mapInitialized = true;
            }
            catch
            {
                // ignore JS errors
            }
        }

        // Încearcă să extragă coordonate (latitudine, longitudine) din textul GPS brut primit de la
        // device, suportând mai multe formate: propoziții NMEA ($GPRMC, $GPGLL), "lat,lon" simplu
        // sau separat prin spațiu. Returnează false dacă niciun format nu poate fi parsat.
        private bool TryParseGpsToLatLon(string gps, out double lat, out double lon)
        {
            lat = 0; lon = 0;
            if (string.IsNullOrWhiteSpace(gps)) return false;

            var lines = gps.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var gprmc = lines.FirstOrDefault(l => l.StartsWith("$GPRMC", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(gprmc))
            {
                var parts = gprmc.Split(',');
                if (parts.Length > 6 && !string.IsNullOrWhiteSpace(parts[3]) && !string.IsNullOrWhiteSpace(parts[5]))
                {
                    if (TryParseNmeaLatLon(parts[3], parts.ElementAtOrDefault(4), parts[5], parts.ElementAtOrDefault(6), out lat, out lon))
                        return true;
                }
            }

            var gpgll = lines.FirstOrDefault(l => l.StartsWith("$GPGLL", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(gpgll))
            {
                var parts = gpgll.Split(',');
                if (parts.Length > 4 && !string.IsNullOrWhiteSpace(parts[1]) && !string.IsNullOrWhiteSpace(parts[3]))
                {
                    if (TryParseNmeaLatLon(parts[1], parts.ElementAtOrDefault(2), parts[3], parts.ElementAtOrDefault(4), out lat, out lon))
                        return true;
                }
            }

            // try plain "lat,lon"
            var first = lines[0].Trim();
            if (first.Contains(','))
            {
                var comps = first.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (comps.Length >= 2 && double.TryParse(comps[0], NumberStyles.Float, CultureInfo.InvariantCulture, out lat) && double.TryParse(comps[1], NumberStyles.Float, CultureInfo.InvariantCulture, out lon))
                    return true;
            }

            // try space separated
            var partsSpace = first.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (partsSpace.Length >= 2 && double.TryParse(partsSpace[0], NumberStyles.Float, CultureInfo.InvariantCulture, out lat) && double.TryParse(partsSpace[1], NumberStyles.Float, CultureInfo.InvariantCulture, out lon))
                return true;

            return false;
        }

        // Convertește componentele de latitudine/longitudine din formatul NMEA (grade + minute zecimale,
        // plus direcție N/S/E/W) în grade decimale standard (ex. pentru afișare pe Google Maps).
        private bool TryParseNmeaLatLon(string latStr, string? latDir, string lonStr, string? lonDir, out double lat, out double lon)
        {
            lat = 0; lon = 0;
            try
            {
                if (string.IsNullOrWhiteSpace(latStr) || string.IsNullOrWhiteSpace(lonStr)) return false;

                latStr = latStr.Trim();
                lonStr = lonStr.Trim();

                int latDp = latStr.IndexOf('.') >= 0 ? latStr.IndexOf('.') : latStr.Length;
                int latDegLen = Math.Max(0, latDp - 2);
                var latDegPart = latStr.Substring(0, latDegLen);
                var latMinPart = latStr.Substring(latDegLen);

                int degLat = int.Parse(latDegPart, CultureInfo.InvariantCulture);
                double minLat = double.Parse(latMinPart, CultureInfo.InvariantCulture);
                lat = degLat + (minLat / 60.0);
                if (!string.IsNullOrWhiteSpace(latDir) && latDir.Trim().Equals("S", StringComparison.OrdinalIgnoreCase)) lat = -lat;

                int lonDp = lonStr.IndexOf('.') >= 0 ? lonStr.IndexOf('.') : lonStr.Length;
                int lonDegLen = Math.Max(0, lonDp - 2);
                var lonDegPart = lonStr.Substring(0, lonDegLen);
                var lonMinPart = lonStr.Substring(lonDegLen);

                int degLon = int.Parse(lonDegPart, CultureInfo.InvariantCulture);
                double minLon = double.Parse(lonMinPart, CultureInfo.InvariantCulture);
                lon = degLon + (minLon / 60.0);
                if (!string.IsNullOrWhiteSpace(lonDir) && lonDir.Trim().Equals("W", StringComparison.OrdinalIgnoreCase)) lon = -lon;

                return true;
            }
            catch
            {
                lat = 0; lon = 0; return false;
            }
        }


        // Inițializează (via JS interop) tooltip-urile interactive ale celor 3 grafice (HR/Temp/SpO2),
        // trimițând punctele de date și formatul de afișare (zecimale, unitate, prefix "Media:" pentru
        // vizualizarea săptămânală care arată valori agregate, nu citiri individuale).
        private async Task InitTooltipsAsync()
        {
            try
            {
                var prefix = CurrentChartView == ChartViewMode.Weekly ? "Media: " : "";
                int hrDecimals = CurrentChartView == ChartViewMode.Weekly ? 1 : 0;

                if (HrTooltipData.Count > 0)
                {
                    var hrData = HrTooltipData.Select(p => new { x = p.X, y = p.Y, value = p.Value, label = p.Label }).ToArray();
                    await JSRuntime.InvokeVoidAsync("chartTooltip.init", _hrSvgRef, "hr", hrData, "#A5D6A7", "bpm", hrDecimals, prefix);
                }

                if (TempTooltipData.Count > 0)
                {
                    var tempData = TempTooltipData.Select(p => new { x = p.X, y = p.Y, value = p.Value, label = p.Label }).ToArray();
                    await JSRuntime.InvokeVoidAsync("chartTooltip.init", _tempSvgRef, "temp", tempData, "#FF9A6C", "°C", 1, prefix);
                }

                if (SpO2TooltipData.Count > 0)
                {
                    var spo2Data = SpO2TooltipData.Select(p => new { x = p.X, y = p.Y, value = p.Value, label = p.Label }).ToArray();
                    int spo2Decimals = CurrentChartView == ChartViewMode.Weekly ? 1 : 0;
                    await JSRuntime.InvokeVoidAsync("chartTooltip.init", _spo2SvgRef, "spo2", spo2Data, "#1565c0", "%", spo2Decimals, prefix);
                }

                _tooltipsInitialized = HrTooltipData.Count > 0 || TempTooltipData.Count > 0 || SpO2TooltipData.Count > 0;
            }
            catch
            {
                // JS interop may fail during prerender
            }
        }

        // Curățenie la distrugerea componentei: marchează pagina ca "disposed" (pentru a ignora
        // evenimente live întârziate), dezabonează handler-ele de evenimente, oprește timer-ul de
        // auto-refresh și elimină resursele JS interop alocate (tooltip-uri, sincronizare scroll).
        public async ValueTask DisposeAsync()
        {
            _disposed = true;
            if (_instance == this) _instance = null;
            if (PushService != null)
            {
                PushService.OnNotificationReceived -= OnPushNotificationReceived;
            }

            MeasurementApiClient.OnMeasurementAdded -= OnMeasurementAdded;
            _refreshTimer?.Dispose();
            try
            {
                await JSRuntime.InvokeVoidAsync("chartTooltip.dispose", "hr");
                await JSRuntime.InvokeVoidAsync("chartTooltip.dispose", "temp");
                await JSRuntime.InvokeVoidAsync("chartTooltip.dispose", "spo2");
                await JSRuntime.InvokeVoidAsync("chartSync.detach", $"selected-{PersonId}");
            }
            catch { }
        }

        // Determină statusul vizual (normal/warning) al unei valori vitale generice (ex. HR) față de
        // intervalul [min, max] configurat; praguri invalide (<=0) sunt tratate ca "fără prag setat".
        private string GetVitalStatus(int value, int min, int max)
        {
            if (min <= 0 || max <= 0) return "normal";
            if (value < min || value > max)
                return "warning";
            return "normal";
        }

        // Variantă textuală (tradusă) a GetVitalStatus, distingând explicit "sub normal" de "peste normal".
        private string GetVitalStatusText(int value, int min, int max)
        {
            if (min <= 0 || max <= 0) return T("vital.normal");
            if (value < min)
                return T("vital.belowNormal");
            if (value > max)
                return T("vital.aboveNormal");
            return T("vital.normal");
        }

        // Echivalentul GetVitalStatus pentru temperatură (valori double).
        private string GetTempStatus(double temp, double min, double max)
        {
            if (min <= 0 || max <= 0) return "normal";
            if (temp < min || temp > max)
                return "warning";
            return "normal";
        }

        private string GetTempStatusText(double temp, double min, double max)
        {
            if (min <= 0 || max <= 0) return T("vital.normal");
            if (temp < min)
                return T("vital.belowNormal");
            if (temp > max)
                return T("vital.aboveNormal");
            return T("vital.normal");
        }

        // SpO2 are 3 niveluri (nu doar normal/warning): sub minSpO2 e "warning", iar sub un prag critic
        // (minSpO2 - 5, dar niciodată sub 70% — limită fiziologică de siguranță) devine "critical".
        // Acest comportament diferă intenționat de GetVitalStatus/GetTempStatus (care au doar 2 nivele),
        // pentru că saturația scăzută a oxigenului este un indicator de urgență medicală mai sensibil.
        private string GetSpO2Status(int spO2, int minSpO2 = 95)
        {
            int critSpO2 = Math.Max(70, minSpO2 - 5);
            if (spO2 > 0 && spO2 < critSpO2) return "critical";
            if (spO2 > 0 && spO2 < minSpO2)  return "warning";
            return "normal";
        }

        // Variantă textuală (tradusă) a GetSpO2Status.
        private string GetSpO2StatusText(int spO2, int minSpO2 = 95)
        {
            int critSpO2 = Math.Max(70, minSpO2 - 5);
            if (spO2 > 0 && spO2 < critSpO2) return T("vital.criticalLow");
            if (spO2 > 0 && spO2 < minSpO2)  return T("vital.belowNormal");
            return T("vital.normal");
        }

        // Indică dacă există semnal GPS valid sau nu (folosit pentru stilizare/iconițe în UI).
        private string GetGPSStatus(string gps)
        {
            if (gps == "No data" || string.IsNullOrEmpty(gps))
                return "disabled";
            return "normal";
        }

        private string GetGPSStatusText(string gps)
        {
            if (gps == "No data" || string.IsNullOrEmpty(gps))
                return "No data available";
            return "Signal available";
        }

        // Simple query-string parser to avoid extra package dependencies
        // Parser minimal pentru query string (ex. ?showEdit=false), implementat manual pentru a evita
        // o dependență de pachet suplimentară doar pentru această nevoie simplă.
        private static System.Collections.Generic.Dictionary<string, string> ParseQueryString(string query)
        {
            var dict = new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(query)) return dict;
            if (query.StartsWith("?")) query = query.Substring(1);
            var parts = query.Split('&', System.StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var kv = part.Split('=', 2);
                try
                {
                    var key = Uri.UnescapeDataString(kv[0]);
                    var value = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : string.Empty;
                    dict[key] = value;
                }
                catch
                {
                    // ignore malformed segments
                }
            }
            return dict;
        }

        // Construiește textul informativ despre expirarea retenției de date pentru această persoană.
        // Are două ramuri distincte: persoană arhivată (se calculează data la care arhiva va fi
        // definitiv eliminată, pe baza ArchivedAt + ArchiveRetentionDays) versus persoană activă
        // (se calculează când datele curente vor expira și vor fi șterse automat, conform politicii
        // de retenție — valoarea implicită de 365 zile trebuie să rămână sincronizată cu
        // RetentionCleanupService.DefaultRetentionDays de pe server).
        private string? GetRetentionExpiryText()
        {
            if (Person == null) return null;
            if (Person.IsArchived)
            {
                if (!Person.ArchiveRetentionDays.HasValue || !Person.ArchivedAt.HasValue) return null;
                var exp = Person.ArchivedAt.Value.ToLocalTime().AddDays(Person.ArchiveRetentionDays.Value);
                var daysLeft = (int)(exp - DateTime.Now).TotalDays;
                return daysLeft <= 0
                    ? T("selected.retentionExpired")
                    : string.Format(T("selected.archiveExpiresOn"), exp.ToString("dd.MM.yyyy"), daysLeft);
            }
            else
            {
                var days = Person.DataRetentionDays ?? 365; // matches RetentionCleanupService.DefaultRetentionDays
                var exp = DateTime.Now.AddDays(days).Date;
                return string.Format(T("selected.dataExpiresOn"), exp.ToString("dd.MM.yyyy"), days);
            }
        }

        // Mapează severitatea unei alerte la o iconiță emoji pentru afișare rapidă în UI.
        private string GetAlertIcon(string severity)
        {
            return severity.ToLower() switch
            {
                "critical" => "⚠️",
                "warning" => "🔔",
                "info" => "ℹ️",
                _ => "🔔"
            };
        }

        // Mapează nivelul de risc returnat de predicția AI la o iconiță emoji.
        private string GetAIRiskIcon(string riskLevel)
        {
            return riskLevel?.ToUpper() switch
            {
                "CRITICAL" => "🚨",
                "ALERT" => "⚠️",
                "NORMAL" => "✅",
                _ => "🤖"
            };
        }

        // Traduce nivelul de risc AI într-un text descriptiv pentru utilizator.
        private string GetAIRiskText(string riskLevel)
        {
            return riskLevel?.ToUpper() switch
            {
                "CRITICAL" => T("selected.aiCritical"),
                "ALERT" => T("selected.aiWarning"),
                "NORMAL" => T("selected.aiOK"),
                _ => riskLevel ?? ""
            };
        }

        // Iconiță pentru metrica de tendință afișată (temperatură/puls/SpO2).
        private static string GetTrendMetricIcon(string metric) => metric switch
        {
            "temperature" => "🌡️",
            "pulse" => "❤️",
            "spo2" => "🩸",
            _ => "📊"
        };

        // Formatează rata de variație a unei metrici (unități/minut) cu semn explicit (+/-) și
        // unitatea corespunzătoare metricii.
        private static string FormatTrendRate(string metric, double ratePerMinute) => metric switch
        {
            "temperature" => $"{(ratePerMinute >= 0 ? "+" : "")}{ratePerMinute:F2} °C/min",
            "pulse" => $"{(ratePerMinute >= 0 ? "+" : "")}{ratePerMinute:F1} bpm/min",
            "spo2" => $"{(ratePerMinute >= 0 ? "+" : "")}{ratePerMinute:F2} %/min",
            _ => $"{ratePerMinute:F2}/min"
        };

        // Formatează un număr de secunde rămase până la atingerea unui prag de alertă, în
        // minute+secunde (dacă >= 60s) sau doar secunde.
        private static string FormatSecondsToThreshold(int seconds, string minLabel, string secLabel)
        {
            if (seconds >= 60)
                return $"~{seconds / 60} {minLabel} {seconds % 60} {secLabel}";
            return $"~{seconds} {secLabel}";
        }

        // Obține eticheta tradusă a unei predicții de tendință; dacă cheia de traducere nu există
        // în resursele de limbă (T returnează aceeași cheie), revine la eticheta brută din DTO.
        private string GetTrendLabel(LifeAlertPlus.Shared.DTOs.Responses.Monitoring.TrendPredictionItemDTO pred)
        {
            var key = $"trend.{pred.Metric}.{pred.Direction}.{pred.Severity}";
            var translated = T(key);
            return translated == key ? pred.Label : translated;
        }

        // Similar cu GetTrendLabel, dar pentru descrierea pragului care a declanșat predicția de tendință.
        private string GetTrendThreshold(LifeAlertPlus.Shared.DTOs.Responses.Monitoring.TrendPredictionItemDTO pred)
        {
            var key = $"trend.threshold.{pred.Metric}.{pred.Direction}";
            var translated = T(key);
            return translated == key ? (pred.ThresholdDescription ?? string.Empty) : translated;
        }

        // Navighează înapoi la lista de persoane monitorizate.
        private void GoBack()
        {
            NavigationManager.NavigateTo("/monitored");
        }

        // Pregătește și deschide modalul de export PDF: determină intervalul de date disponibil
        // (cea mai veche și cea mai recentă măsurătoare) pentru a oferi valori implicite sensibile
        // în selectorul de interval de export.
        // Flux: MeasurementApiClient.GetMeasurementsByMonitoredIdAsync(PersonId, 1, 10000)
        //   → HTTP GET api/measurement/monitored/{PersonId}?pageNumber=1&pageSize=10000
        //   → MeasurementController.GetByMonitoredId()
        //   → IMeasurementService → IMeasurementRepository → DB
        //   → min/max date extrase local din lista returnată, fără un al doilea apel API
        private async Task ExportPdfAsync()
        {
            if (Person == null) return;

            try
            {
                // Fetch all measurements to determine date range
                var allMeasurements = await MeasurementApiClient.GetMeasurementsByMonitoredIdAsync(PersonId, 1, 10000);
                var list = (allMeasurements ?? Enumerable.Empty<MeasurementResponseDTO>()).ToList();

                if (list.Count == 0)
                {
                    _exportMinDate = DateTime.Today;
                    _exportMaxDate = DateTime.Today;
                }
                else
                {
                    _exportMinDate = list.Min(m => m.CreatedAt).ToLocalTime().Date;
                    _exportMaxDate = list.Max(m => m.CreatedAt).ToLocalTime().Date;
                }

                _exportStartDate = _exportMinDate;
                _exportEndDate = _exportMaxDate;
                _exportMeasurementCount = list.Count;
                _exportDistinctDays = list.Select(m => m.CreatedAt.ToLocalTime().Date).Distinct().Count();
                _showExportModal = true;
                StateHasChanged();
            }
            catch (Exception) { }
        }

        private void CloseExportModal()
        {
            _showExportModal = false;
        }

        // Apelabil din JavaScript (ex. dintr-un buton injectat în PDF/raport sau dintr-un link extern) —
        // deschide modalul de trimitere a raportului pe email, operând pe instanța statică curentă.
        [JSInvokable]
        public static Task OpenEmailModal()
        {
            if (_instance != null)
            {
                _instance._doctorEmail = string.Empty;
                _instance._emailStatusMessage = null;
                _instance._showEmailModal = true;
                _instance.StateHasChanged();
            }
            return Task.CompletedTask;
        }

        private void CloseEmailModal()
        {
            _showEmailModal = false;
            _emailStatusMessage = null;
        }

        // Deschide modalul de invitare a unui medic (acces la datele acestei persoane monitorizate).
        private void OpenInviteModal()
        {
            _inviteDoctorEmail = string.Empty;
            _inviteStatusMessage = null;
            _inviteSuccess = false;
            _showInviteModal = true;
        }

        private void CloseInviteModal()
        {
            _showInviteModal = false;
            _inviteStatusMessage = null;
        }

        // Trimite o invitație (link de acces) către emailul unui medic, validând minimal formatul
        // adresei înainte de a apela API-ul.
        // Flux: HTTP POST api/email/send-doctor-invitation { DoctorEmail, PatientId, PatientName }
        //   → EmailController.SendDoctorInvitation()
        //   → generare token securizat (SHA-256), stocat în Invitations tabel
        //   → IEmailService.SendDoctorInvitationEmailAsync() → SMTP
        //   → returnează InvitationResponseDTO cu link /invite/patient?token=...
        private async Task SendInvitationToDoctorAsync()
        {
            if (Person == null) return;

            _inviteStatusMessage = null;
            _inviteSuccess = false;

            if (string.IsNullOrWhiteSpace(_inviteDoctorEmail) || !_inviteDoctorEmail.Contains('@'))
            {
                _inviteStatusMessage = T("invite.invitationSendError");
                _inviteSuccess = false;
                return;
            }

            _isSendingInvitation = true;
            StateHasChanged();

            try
            {
                var dto = new SendDoctorInvitationRequestDTO
                {
                    DoctorEmail = _inviteDoctorEmail.Trim(),
                    PatientId = PersonId,
                    PatientName = $"{Person.FirstName} {Person.LastName}".Trim()
                };

                var response = await HttpClient.PostAsJsonAsync("api/email/send-doctor-invitation", dto);
                if (response.IsSuccessStatusCode)
                {
                    _inviteStatusMessage = T("invite.invitationSent");
                    _inviteSuccess = true;
                }
                else
                {
                    _inviteStatusMessage = T("invite.invitationSendError");
                    _inviteSuccess = false;
                }
            }
            catch (Exception)
            {
                _inviteStatusMessage = T("invite.invitationSendError");
                _inviteSuccess = false;
            }
            finally
            {
                _isSendingInvitation = false;
                StateHasChanged();
            }
        }

        // Deschide modalul WiFi și încarcă lista curentă de rețele salvate pe device-ul ESP.
        // Flux: WifiApiClient.GetByMonitoredAsync(PersonId)
        //   → HTTP GET api/wifi/monitored/{PersonId}
        //   → WifiController.GetByMonitored()
        //   → IWifiNetworkService → WifiNetworkRepository → DB
        //   → returnează List<WifiNetworkResponseDTO> (fără parole)
        private async Task OpenWifiModalAsync()
        {
            _newWifiSsid = string.Empty;
            _newWifiPassword = string.Empty;
            _wifiStatusMessage = null;
            _wifiSuccess = false;
            _showWifiModal = true;
            _isWifiLoading = true;
            StateHasChanged();

            try
            {
                _wifiNetworks = await WifiApiClient.GetByMonitoredAsync(PersonId);
            }
            finally
            {
                _isWifiLoading = false;
                StateHasChanged();
            }
        }

        private void CloseWifiModal()
        {
            _showWifiModal = false;
            _wifiStatusMessage = null;
            _newWifiSsid = string.Empty;
            _newWifiPassword = string.Empty;
        }

        // Adaugă o rețea WiFi nouă pentru device — blocat client-side dacă SSID-ul e vid sau dacă
        // s-a atins deja numărul maxim de rețele permise (MaxWifiNetworks); validări suplimentare
        // (SSID duplicat, lungime, limită) sunt verificate și pe server, ale cărui erori sunt mapate
        // mai jos la mesaje traduse.
        // Flux: WifiApiClient.AddAsync(PersonId, ssid, password)
        //   → HTTP POST api/wifi { IdMonitored, Ssid, Password }
        //   → WifiController.Add()
        //   → IWifiNetworkService.AddAsync() → WifiNetworkRepository → DB
        //   → returnează WifiNetworkResponseDTO (fără parolă) sau { Error: "ssidDuplicate"|"limitReached"|... }
        private async Task AddWifiNetworkAsync()
        {
            if (string.IsNullOrWhiteSpace(_newWifiSsid) || _wifiNetworks.Count >= MaxWifiNetworks)
                return;

            _isAddingWifi = true;
            _wifiStatusMessage = null;
            StateHasChanged();

            try
            {
                var (success, errorKey, network) = await WifiApiClient.AddAsync(PersonId, _newWifiSsid.Trim(), _newWifiPassword ?? string.Empty);
                if (success && network != null)
                {
                    _wifiNetworks.Add(network);
                    _newWifiSsid = string.Empty;
                    _newWifiPassword = string.Empty;
                    _wifiSuccess = true;
                    _wifiStatusMessage = T("wifi.added");
                }
                else
                {
                    _wifiSuccess = false;
                    _wifiStatusMessage = errorKey switch
                    {
                        "ssidRequired" => T("wifi.errSsidRequired"),
                        "ssidTooLong" => T("wifi.errSsidTooLong"),
                        "passwordTooLong" => T("wifi.errPasswordTooLong"),
                        "ssidDuplicate" => T("wifi.errSsidDuplicate"),
                        "limitReached" => T("wifi.errLimitReached"),
                        _ => T("wifi.errGeneric")
                    };
                }
            }
            catch
            {
                _wifiSuccess = false;
                _wifiStatusMessage = T("wifi.errGeneric");
            }
            finally
            {
                _isAddingWifi = false;
                StateHasChanged();
            }
        }

        // Șterge o rețea WiFi salvată a device-ului, după id.
        // Flux: WifiApiClient.DeleteAsync(id)
        //   → HTTP DELETE api/wifi/{id}
        //   → WifiController.Delete()
        //   → IWifiNetworkService.DeleteAsync() → WifiNetworkRepository → DB
        private async Task DeleteWifiNetworkAsync(Guid id)
        {
            var ok = await WifiApiClient.DeleteAsync(id);
            if (ok)
            {
                _wifiNetworks.RemoveAll(n => n.Id == id);
                _wifiSuccess = true;
                _wifiStatusMessage = T("wifi.deleted");
                StateHasChanged();
            }
        }

        // Trimite raportul PDF curent (deja generat în pagină, ca PDF client-side) pe emailul medicului.
        // PDF-ul este extras ca Base64 direct din componenta JS de export, fără regenerare server-side.
        // Flux: JSRuntime.InvokeAsync("pdfExport.getPdfBase64") → base64 string
        //   → HTTP POST api/email/send-report { doctorEmail, patientName, pdfBase64 }
        //   → EmailController.SendReport()
        //   → IEmailService.SendReportEmailAsync(email, numePatient, pdfBytes) → SMTP
        private async Task SendEmailToDoctorAsync()
        {
            if (string.IsNullOrWhiteSpace(_doctorEmail) || Person == null) return;
            _isSendingEmail = true;
            _emailStatusMessage = null;
            StateHasChanged();

            try
            {
                var base64 = await JSRuntime.InvokeAsync<string>("pdfExport.getPdfBase64");
                if (string.IsNullOrEmpty(base64))
                {
                    _emailStatusMessage = T("email.sendError");
                    _emailSuccess = false;
                    return;
                }

                var payload = new
                {
                    doctorEmail = _doctorEmail,
                    patientName = $"{Person.FirstName} {Person.LastName}",
                    pdfBase64 = base64
                };

                var response = await HttpClient.PostAsJsonAsync("api/email/send-report", payload);
                if (response.IsSuccessStatusCode)
                {
                    _emailStatusMessage = T("email.sent");
                    _emailSuccess = true;
                }
                else
                {
                    _emailStatusMessage = T("email.sendError");
                    _emailSuccess = false;
                }
            }
            catch (Exception)
            {
                _emailStatusMessage = T("email.sendError");
                _emailSuccess = false;
            }
            finally
            {
                _isSendingEmail = false;
                StateHasChanged();
            }
        }

        // Construiește structura de date completă pentru raportul PDF exportabil: statistici generale,
        // detaliere săptămânală/zilnică, liste de alerte și evenimente critice, și datele brute —
        // toate calculate client-side din măsurătorile filtrate pe intervalul de date selectat.
        // Flux: MeasurementApiClient.GetMeasurementsByMonitoredIdAsync(PersonId, 1, 10000)
        //   → HTTP GET api/measurement/monitored/{PersonId}?pageNumber=1&pageSize=10000
        //   → date filtrate și agregate local (fără apel API suplimentar)
        //   → JSRuntime.InvokeVoidAsync("pdfExport.generateMedicalReport", pdfData)
        //      → generare PDF client-side în browser via jsPDF/html2pdf
        private async Task GenerateExportPdfAsync()
        {
            if (Person == null) return;
            _isExporting = true;
            StateHasChanged();

            try
            {
                var allMeasurements = await MeasurementApiClient.GetMeasurementsByMonitoredIdAsync(PersonId, 1, 10000);
                var filtered = (allMeasurements ?? Enumerable.Empty<MeasurementResponseDTO>())
                    .Where(m =>
                    {
                        var local = m.CreatedAt.ToLocalTime().Date;
                        return (!_exportStartDate.HasValue || local >= _exportStartDate.Value.Date)
                            && (!_exportEndDate.HasValue || local <= _exportEndDate.Value.Date);
                    })
                    .OrderBy(m => m.CreatedAt)
                    .ToList();

                var periodLabel = "";
                if (_exportStartDate.HasValue && _exportEndDate.HasValue)
                {
                    periodLabel = _exportStartDate.Value.Date == _exportEndDate.Value.Date
                        ? $"{_exportStartDate.Value:dd MMM yyyy}"
                        : $"{_exportStartDate.Value:dd MMM yyyy} - {_exportEndDate.Value:dd MMM yyyy}";
                }

                // --- Summary stats over entire period ---
                // Statistici agregate (medie, min, max, deviație standard) pentru întreaga perioadă exportată.
                object? summary = null;
                if (filtered.Count > 0)
                {
                    var pulses = filtered.Select(m => m.Pulse).ToList();
                    var temps = filtered.Select(m => m.Temperature).ToList();
                    var spo2s = filtered.Select(m => m.SpO2).ToList();
                    summary = new
                    {
                        totalMeasurements = filtered.Count,
                        pulseAvg = $"{pulses.Average():F1} bpm",
                        pulseMin = $"{pulses.Min():F0} bpm",
                        pulseMax = $"{pulses.Max():F0} bpm",
                        pulseStdDev = $"{StdDev(pulses):F2}",
                        tempAvg = $"{temps.Average():F2} C",
                        tempMin = $"{temps.Min():F1} C",
                        tempMax = $"{temps.Max():F1} C",
                        tempStdDev = $"{StdDev(temps):F2}",
                        spo2Avg = $"{spo2s.Average():F1}",
                        spo2Min = $"{spo2s.Min():F1}",
                        spo2Max = $"{spo2s.Max():F1}",
                        spo2StdDev = $"{StdDev(spo2s):F2}"
                    };
                }

                // --- Weekly breakdown ---
                // Defalcare statistici pe săptămâni calendaristice (regula "FirstFourDayWeek", luni ca prima zi).
                var weeklyBreakdown = filtered
                    .GroupBy(m =>
                    {
                        var local = m.CreatedAt.ToLocalTime();
                        var cal = System.Globalization.CultureInfo.CurrentCulture.Calendar;
                        var weekOfYear = cal.GetWeekOfYear(local, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
                        return new { local.Year, Week = weekOfYear };
                    })
                    .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Week)
                    .Select(g =>
                    {
                        var items = g.ToList();
                        var pulses = items.Select(m => m.Pulse).ToList();
                        var temps = items.Select(m => m.Temperature).ToList();
                        var spo2s = items.Select(m => m.SpO2).ToList();
                        var first = items.Min(m => m.CreatedAt).ToLocalTime();
                        var last = items.Max(m => m.CreatedAt).ToLocalTime();
                        return new
                        {
                            weekLabel = $"{first:dd MMM} – {last:dd MMM yyyy}",
                            count = items.Count,
                            pulseAvg = $"{pulses.Average():F1}",
                            pulseMin = $"{pulses.Min():F0}",
                            pulseMax = $"{pulses.Max():F0}",
                            pulseStdDev = $"{StdDev(pulses):F2}",
                            tempAvg = $"{temps.Average():F2}",
                            tempMin = $"{temps.Min():F1}",
                            tempMax = $"{temps.Max():F1}",
                            tempStdDev = $"{StdDev(temps):F2}",
                            spo2Avg = $"{spo2s.Average():F1}",
                            spo2Min = $"{spo2s.Min():F1}",
                            spo2Max = $"{spo2s.Max():F1}",
                            spo2StdDev = $"{StdDev(spo2s):F2}"
                        };
                    })
                    .ToArray();

                // --- Daily breakdown ---
                // Defalcare statistici pe zi calendaristică.
                var dailyBreakdown = filtered
                    .GroupBy(m => m.CreatedAt.ToLocalTime().Date)
                    .OrderBy(g => g.Key)
                    .Select(g =>
                    {
                        var items = g.ToList();
                        var pulses = items.Select(m => m.Pulse).ToList();
                        var temps = items.Select(m => m.Temperature).ToList();
                        var spo2s = items.Select(m => m.SpO2).ToList();
                        return new
                        {
                            date = g.Key.ToString("dd MMM yyyy"),
                            count = items.Count,
                            pulseAvg = $"{pulses.Average():F1}",
                            pulseMin = $"{pulses.Min():F0}",
                            pulseMax = $"{pulses.Max():F0}",
                            tempAvg = $"{temps.Average():F2}",
                            tempMin = $"{temps.Min():F1}",
                            tempMax = $"{temps.Max():F1}",
                            spo2Avg = $"{spo2s.Average():F1}",
                            spo2Min = $"{spo2s.Min():F1}",
                            spo2Max = $"{spo2s.Max():F1}"
                        };
                    })
                    .ToArray();

                // --- Alerts (measurements outside normal thresholds) ---
                // Măsurătorile din raport care ies din pragurile normale ale persoanei (HR/Temp în afara
                // intervalului configurat, sau SpO2 sub 95% — prag fix folosit aici, distinct de pragul
                // personalizat MinSpO2 al persoanei).
                var alerts = filtered
                    .Where(m =>
                        (m.Pulse < Person.MinHeartRate || m.Pulse > Person.MaxHeartRate) ||
                        (m.Temperature < Person.MinTemperature || m.Temperature > Person.MaxTemperature) ||
                        (m.SpO2 > 0 && m.SpO2 < 95))
                    .Select(m =>
                    {
                        var reasons = new List<string>();
                        if (m.Pulse < Person.MinHeartRate) reasons.Add($"HR low ({m.Pulse:F0} bpm)");
                        else if (m.Pulse > Person.MaxHeartRate) reasons.Add($"HR high ({m.Pulse:F0} bpm)");
                        if (m.Temperature < Person.MinTemperature) reasons.Add($"Temp low ({m.Temperature:F1} C)");
                        else if (m.Temperature > Person.MaxTemperature) reasons.Add($"Temp high ({m.Temperature:F1} C)");
                        if (m.SpO2 > 0 && m.SpO2 < 95) reasons.Add($"SpO2 low ({m.SpO2:F1})");
                        return new
                        {
                            date = m.CreatedAt.ToLocalTime().ToString("dd MMM yyyy HH:mm"),
                            reason = string.Join(", ", reasons),
                            pulse = $"{m.Pulse:F0} bpm",
                            temperature = $"{m.Temperature:F1} C",
                            spo2 = m.SpO2 > 0 ? $"{m.SpO2:F1}" : "-"
                        };
                    })
                    .ToArray();

                // --- Critical events (falls + extreme values) ---
                // Evenimente considerate critice pentru raport: cădere detectată, sau abateri severe
                // de la pragurile persoanei (HR cu marje suplimentare mari de -20/+30 bpm, temperatură
                // cu ±1/1.5°C peste limite — hipotermie/hipertermie —, ori SpO2 sub 90%, prag medical
                // general de hipoxemie severă). Marjele sunt distincte (mai largi) decât cele din
                // ComputeStatus, fiind calibrate pentru raportare istorică, nu pentru alertare live.
                var criticals = filtered
                    .Where(m =>
                        m.IsFall ||
                        m.Pulse < Person.MinHeartRate - 20 || m.Pulse > Person.MaxHeartRate + 30 ||
                        m.Temperature < Person.MinTemperature - 1 || m.Temperature > Person.MaxTemperature + 1.5 ||
                        (m.SpO2 > 0 && m.SpO2 < 90))
                    .Select(m =>
                    {
                        var reasons = new List<string>();
                        if (m.IsFall) reasons.Add("Fall detected");
                        if (m.Pulse < Person.MinHeartRate - 20) reasons.Add($"HR critically low ({m.Pulse:F0} bpm)");
                        if (m.Pulse > Person.MaxHeartRate + 30) reasons.Add($"HR critically high ({m.Pulse:F0} bpm)");
                        if (m.Temperature < Person.MinTemperature - 1) reasons.Add($"Hypothermia ({m.Temperature:F1} C)");
                        if (m.Temperature > Person.MaxTemperature + 1.5) reasons.Add($"Hyperthermia ({m.Temperature:F1} C)");
                        if (m.SpO2 > 0 && m.SpO2 < 90) reasons.Add($"SpO2 critical ({m.SpO2:F1})");
                        return new
                        {
                            date = m.CreatedAt.ToLocalTime().ToString("dd MMM yyyy HH:mm"),
                            reason = string.Join(", ", reasons),
                            pulse = $"{m.Pulse:F0} bpm",
                            temperature = $"{m.Temperature:F1} C",
                            spo2 = m.SpO2 > 0 ? $"{m.SpO2:F1}" : "-"
                        };
                    })
                    .ToArray();

                // --- Raw data rows ---
                // Toate măsurătorile brute din intervalul selectat, formatate pentru tabelul detaliat din raport.
                var rawData = filtered
                    .Select(m => new
                    {
                        date = m.CreatedAt.ToLocalTime().ToString("dd MMM yyyy HH:mm"),
                        pulse = $"{m.Pulse:F0} bpm",
                        temperature = $"{m.Temperature:F1} C",
                        spo2 = m.SpO2 > 0 ? $"{m.SpO2:F1}" : "-",
                        activity = m.Activity ?? "-",
                        fall = m.IsFall ? "Yes" : "-"
                    })
                    .ToArray();

                // --- Automatic Interpretation (severity-aware) ---
                // Each item: { text, plain, severity } where severity = "low" | "medium" | "high"
                // Secțiune de "interpretare clinică automată" a raportului — calculează un scor de risc
                // agregat (0-100) și o serie de observații textuale pe baza statisticilor perioadei,
                // analizând separat HR, temperatură, SpO2 și evenimentele (alerte/cădere/critice).
                // NU este un diagnostic medical — este un sumar euristic destinat să ajute medicul/
                // îngrijitorul să identifice rapid zonele de îngrijorare din perioada raportată.
                var interpretationItems = new List<object>();
                int riskScore = 0;
                string riskLevel = "LOW";
                var riskBreakdown = new List<object>(); // { factor, points }
                var topConcerns = new List<object>();    // { rank, text, severity }
                string dataConfidence = "LOW";
                string dataConfidenceNote = "";
                if (filtered.Count > 0)
                {
                    var avgPulse = filtered.Average(m => m.Pulse);
                    var avgTemp = filtered.Average(m => m.Temperature);
                    var avgSpo2 = filtered.Average(m => m.SpO2);
                    var minPulse = filtered.Min(m => m.Pulse);
                    var maxPulse = filtered.Max(m => m.Pulse);
                    var minTemp = filtered.Min(m => m.Temperature);
                    var maxTemp = filtered.Max(m => m.Temperature);
                    var minSpo2 = filtered.Min(m => m.SpO2);
                    var fallCount = filtered.Count(m => m.IsFall);
                    var totalDays = filtered.Select(m => m.CreatedAt.ToLocalTime().Date).Distinct().Count();

                    // ── Data Confidence ──
                    // Nivelul de încredere în concluziile raportului depinde de volumul de date
                    // disponibil: HIGH necesită minim 30 măsurători pe minim 7 zile distincte,
                    // MEDIUM minim 14 măsurători pe minim 3 zile, altfel LOW (date insuficiente).
                    if (filtered.Count >= 30 && totalDays >= 7)
                    { dataConfidence = "HIGH"; dataConfidenceNote = T("export.confidence.high").Replace("{0}", $"{filtered.Count}").Replace("{1}", $"{totalDays}"); }
                    else if (filtered.Count >= 14 && totalDays >= 3)
                    { dataConfidence = "MEDIUM"; dataConfidenceNote = T("export.confidence.medium").Replace("{0}", $"{filtered.Count}").Replace("{1}", $"{totalDays}"); }
                    else
                    { dataConfidence = "LOW"; dataConfidenceNote = T("export.confidence.low").Replace("{0}", $"{filtered.Count}").Replace("{1}", $"{totalDays}"); }

                    // ── Heart Rate ──
                    // Evaluează HR mediu față de pragurile persoanei și detectează vârfuri/scăderi
                    // izolate (max/min peste/sub praguri) chiar dacă media e normală — fiecare scenariu
                    // contribuie diferit la riskScore (penalizare mai mare pentru abateri severe, >30bpm/-20bpm).
                    bool hrAvgOk = avgPulse >= Person.MinHeartRate && avgPulse <= Person.MaxHeartRate;
                    bool hrHasSpike = maxPulse > Person.MaxHeartRate;
                    bool hrHasDip = minPulse < Person.MinHeartRate;

                    if (hrAvgOk && !hrHasSpike && !hrHasDip)
                    {
                        interpretationItems.Add(new { text = T("export.interp.hrNormal").Replace("{0}", $"{avgPulse:F0}"), plain = T("export.plain.hrNormal"), severity = "low" });
                    }
                    else if (hrAvgOk && (hrHasSpike || hrHasDip))
                    {
                        var spikeTxt = hrHasSpike
                            ? T("export.interp.hrNormalWithSpikes").Replace("{0}", $"{avgPulse:F0}").Replace("{1}", $"{maxPulse:F0}")
                            : T("export.interp.hrNormalWithDips").Replace("{0}", $"{avgPulse:F0}").Replace("{1}", $"{minPulse:F0}");
                        var plainTxt = hrHasSpike
                            ? T("export.plain.hrSpike").Replace("{0}", $"{maxPulse:F0}")
                            : T("export.plain.hrDip").Replace("{0}", $"{minPulse:F0}");
                        var sev = maxPulse > Person.MaxHeartRate + 30 || minPulse < Person.MinHeartRate - 20 ? "high" : "medium";
                        interpretationItems.Add(new { text = spikeTxt, plain = plainTxt, severity = sev });
                        var pts = sev == "high" ? 15 : 8;
                        riskScore += pts;
                        riskBreakdown.Add(new { factor = T("export.risk.hrSpikes"), points = pts });
                    }
                    else if (avgPulse < Person.MinHeartRate)
                    {
                        interpretationItems.Add(new { text = T("export.interp.hrLow").Replace("{0}", $"{avgPulse:F0}"), plain = T("export.plain.hrLowSimple"), severity = "medium" });
                        riskScore += 10;
                        riskBreakdown.Add(new { factor = T("export.risk.hrLow"), points = 10 });
                    }
                    else
                    {
                        interpretationItems.Add(new { text = T("export.interp.hrHigh").Replace("{0}", $"{avgPulse:F0}"), plain = T("export.plain.hrHighSimple"), severity = "medium" });
                        riskScore += 10;
                        riskBreakdown.Add(new { factor = T("export.risk.hrHigh"), points = 10 });
                    }

                    // O variabilitate mare a pulsului (diferență peste 50 bpm între min și max în perioadă)
                    // este semnalată separat, indiferent de mediile individuale fiind normale.
                    if (maxPulse - minPulse > 50)
                    {
                        interpretationItems.Add(new { text = T("export.interp.hrVariability").Replace("{0}", $"{minPulse:F0}").Replace("{1}", $"{maxPulse:F0}"), plain = T("export.plain.hrVariability"), severity = "medium" });
                        riskScore += 5;
                        riskBreakdown.Add(new { factor = T("export.risk.hrVariability"), points = 5 });
                    }

                    // ── Temperature ──
                    // Notă: aici se folosește un singur prag de febră (MaxTemperature), nu un prag separat
                    // de hipotermie distinct ca în secțiunea HR — abaterea sub minim e tratată în ramura "else".
                    bool tempAvgOk = avgTemp >= Person.MinTemperature && avgTemp <= Person.MaxTemperature;
                    bool tempHasSpike = maxTemp > Person.MaxTemperature;

                    if (tempAvgOk && !tempHasSpike)
                    {
                        interpretationItems.Add(new { text = T("export.interp.tempNormal").Replace("{0}", $"{avgTemp:F1}"), plain = T("export.plain.tempNormal"), severity = "low" });
                    }
                    else if (tempAvgOk && tempHasSpike)
                    {
                        var sev = maxTemp > Person.MaxTemperature + 1.0 ? "high" : "medium";
                        interpretationItems.Add(new { text = T("export.interp.tempNormalWithSpikes").Replace("{0}", $"{avgTemp:F1}").Replace("{1}", $"{maxTemp:F1}"), plain = T("export.plain.tempSpike").Replace("{0}", $"{maxTemp:F1}"), severity = sev });
                        var pts = sev == "high" ? 12 : 6;
                        riskScore += pts;
                        riskBreakdown.Add(new { factor = T("export.risk.fever"), points = pts });
                    }
                    else if (avgTemp > Person.MaxTemperature)
                    {
                        interpretationItems.Add(new { text = T("export.interp.tempHigh").Replace("{0}", $"{avgTemp:F1}"), plain = T("export.plain.tempHighSimple"), severity = "high" });
                        riskScore += 12;
                        riskBreakdown.Add(new { factor = T("export.risk.tempHigh"), points = 12 });
                    }
                    else
                    {
                        interpretationItems.Add(new { text = T("export.interp.tempLow").Replace("{0}", $"{avgTemp:F1}"), plain = T("export.plain.tempLowSimple"), severity = "medium" });
                        riskScore += 8;
                        riskBreakdown.Add(new { factor = T("export.risk.tempLow"), points = 8 });
                    }

                    // ── SpO2 ──
                    // Pentru SpO2 se folosește pragul fix de 95% (nu pragul personalizat al persoanei) ca
                    // referință de normalitate, iar sub 90% media e considerată "critică" (risc maxim: 20 puncte) —
                    // reflectă convenția medicală generală de hipoxemie severă.
                    bool spo2AvgOk = avgSpo2 >= 95;
                    bool spo2HasDip = minSpo2 < 95;

                    if (spo2AvgOk && !spo2HasDip)
                    {
                        interpretationItems.Add(new { text = T("export.interp.spo2Normal").Replace("{0}", $"{avgSpo2:F1}"), plain = T("export.plain.spo2Normal"), severity = "low" });
                    }
                    else if (spo2AvgOk && spo2HasDip)
                    {
                        var sev = minSpo2 < 90 ? "high" : "medium";
                        interpretationItems.Add(new { text = T("export.interp.spo2NormalWithDips").Replace("{0}", $"{avgSpo2:F1}").Replace("{1}", $"{minSpo2:F1}"), plain = T("export.plain.spo2Dip").Replace("{0}", $"{minSpo2:F1}"), severity = sev });
                        var pts = sev == "high" ? 15 : 6;
                        riskScore += pts;
                        riskBreakdown.Add(new { factor = T("export.risk.spo2Dips"), points = pts });
                    }
                    else if (avgSpo2 >= 90)
                    {
                        interpretationItems.Add(new { text = T("export.interp.spo2Low").Replace("{0}", $"{avgSpo2:F1}"), plain = T("export.plain.spo2LowSimple"), severity = "medium" });
                        riskScore += 10;
                        riskBreakdown.Add(new { factor = T("export.risk.spo2Low"), points = 10 });
                    }
                    else if (avgSpo2 > 0)
                    {
                        interpretationItems.Add(new { text = T("export.interp.spo2Critical").Replace("{0}", $"{avgSpo2:F1}"), plain = T("export.plain.spo2CriticalSimple"), severity = "high" });
                        riskScore += 20;
                        riskBreakdown.Add(new { factor = T("export.risk.spo2Critical"), points = 20 });
                    }

                    // ── Events ──
                    // Numărul de alerte/evenimente critice/cădere detectate contribuie suplimentar la scor,
                    // cu puncte plafonate per categorie (ex. alertele contribuie max 15 puncte în total,
                    // indiferent câte sunt) pentru a evita ca un volum mare de alerte minore să domine scorul.
                    if (alerts.Length > 0)
                    {
                        var sev = alerts.Length > 10 ? "medium" : "low";
                        interpretationItems.Add(new { text = T("export.interp.alertsFound").Replace("{0}", $"{alerts.Length}").Replace("{1}", $"{totalDays}"), plain = T("export.plain.alerts").Replace("{0}", $"{alerts.Length}"), severity = sev });
                        var pts = Math.Min(alerts.Length * 2, 15);
                        riskScore += pts;
                        riskBreakdown.Add(new { factor = $"{alerts.Length} {T("export.risk.alerts")}", points = pts });
                    }
                    if (criticals.Length > 0)
                    {
                        interpretationItems.Add(new { text = T("export.interp.criticalsFound").Replace("{0}", $"{criticals.Length}"), plain = T("export.plain.criticals").Replace("{0}", $"{criticals.Length}"), severity = "high" });
                        var pts = criticals.Length * 8;
                        riskScore += pts;
                        riskBreakdown.Add(new { factor = $"{criticals.Length} {T("export.risk.criticals")}", points = pts });
                    }
                    if (fallCount > 0)
                    {
                        interpretationItems.Add(new { text = T("export.interp.fallsDetected").Replace("{0}", $"{fallCount}"), plain = T("export.plain.falls").Replace("{0}", $"{fallCount}"), severity = "high" });
                        var pts = fallCount * 10;
                        riskScore += pts;
                        riskBreakdown.Add(new { factor = $"{fallCount} {T("export.risk.falls")}", points = pts });
                    }

                    if (alerts.Length == 0 && criticals.Length == 0 && fallCount == 0)
                        interpretationItems.Add(new { text = T("export.interp.noIncidents"), plain = T("export.plain.noIncidents"), severity = "low" });

                    // ── Risk Score (cap at 100) ──
                    // Scorul agregat este plafonat la 100 și mapat în 3 niveluri de risc:
                    // HIGH (>=60), MEDIUM (>=30), LOW (sub 30).
                    riskScore = Math.Min(riskScore, 100);
                    riskLevel = riskScore >= 60 ? "HIGH" : riskScore >= 30 ? "MEDIUM" : "LOW";

                    // ── Top Concerns (ranked by severity, max 3) ──
                    // Selectează cele mai importante 3 motive de îngrijorare, în ordine de prioritate
                    // fixă (cădere + alt eveniment sever > cădere singură > evenimente critice > febră >
                    // tahicardie/bradicardie > SpO2 scăzut), pentru un sumar rapid, ușor de citit de medic.
                    int rank = 0;
                    if (fallCount > 0 && (hrHasSpike || tempHasSpike))
                    {
                        topConcerns.Add(new { rank = ++rank, text = T("export.concern.fallWithSpike").Replace("{0}", $"{maxPulse:F0}").Replace("{1}", $"{maxTemp:F1}"), severity = "high" });
                    }
                    else if (fallCount > 0)
                    {
                        topConcerns.Add(new { rank = ++rank, text = T("export.concern.fall").Replace("{0}", $"{fallCount}"), severity = "high" });
                    }
                    if (criticals.Length > 0 && rank == 0)
                    {
                        topConcerns.Add(new { rank = ++rank, text = T("export.concern.criticals").Replace("{0}", $"{criticals.Length}"), severity = "high" });
                    }
                    if (tempHasSpike && maxTemp > Person.MaxTemperature + 0.5)
                    {
                        topConcerns.Add(new { rank = ++rank, text = T("export.concern.fever").Replace("{0}", $"{maxTemp:F1}"), severity = maxTemp > Person.MaxTemperature + 1.0 ? "high" : "medium" });
                    }
                    if (hrHasSpike && maxPulse > Person.MaxHeartRate + 10)
                    {
                        topConcerns.Add(new { rank = ++rank, text = T("export.concern.hrSpike").Replace("{0}", $"{maxPulse:F0}"), severity = maxPulse > Person.MaxHeartRate + 30 ? "high" : "medium" });
                    }
                    if (spo2HasDip && minSpo2 < 93)
                    {
                        topConcerns.Add(new { rank = ++rank, text = T("export.concern.spo2").Replace("{0}", $"{minSpo2:F1}"), severity = minSpo2 < 90 ? "high" : "medium" });
                    }
                    // Limit to top 3
                    if (topConcerns.Count > 3) topConcerns = topConcerns.Take(3).ToList();
                }

                // --- Conclusion (clinical synthesis) ---
                // Sintetizează concluziile raportului într-o listă de paragrafe text: prezentare generală,
                // detectare de episoade acute (cădere combinată cu HR/temperatură anormale), verdict global
                // (stabil/monitorizare/risc ridicat pe baza riskLevel calculat mai sus), urmat de un rezumat
                // al evenimentelor critice/alertelor/căderilor și valorile de vârf înregistrate.
                var conclusionParts = new List<string>();
                if (filtered.Count > 0)
                {
                    var avgPulse = filtered.Average(m => m.Pulse);
                    var avgTemp = filtered.Average(m => m.Temperature);
                    var avgSpo2 = filtered.Average(m => m.SpO2);
                    var maxPulse = filtered.Max(m => m.Pulse);
                    var maxTemp = filtered.Max(m => m.Temperature);
                    var minSpo2 = filtered.Min(m => m.SpO2);
                    var totalDays = filtered.Select(m => m.CreatedAt.ToLocalTime().Date).Distinct().Count();
                    var fallCount = filtered.Count(m => m.IsFall);

                    conclusionParts.Add(T("export.conclusion.overview")
                        .Replace("{0}", $"{filtered.Count}")
                        .Replace("{1}", $"{totalDays}"));

                    // Detect acute episodes
                    // Un "episod acut" este definit ca o cădere însoțită de o anomalie semnificativă de
                    // HR sau temperatură — combinația indică un eveniment potențial grav, nu doar o
                    // simplă cădere accidentală fără alte semne vitale alarmante.
                    bool hasFalls = fallCount > 0;
                    bool hasCriticalHR = maxPulse > Person.MaxHeartRate + 30;
                    bool hasFever = maxTemp > Person.MaxTemperature + 0.5;
                    bool hasCriticalSpo2 = minSpo2 < 90;

                    if (hasFalls && (hasCriticalHR || hasFever))
                    {
                        conclusionParts.Add(T("export.conclusion.acuteEpisode")
                            .Replace("{0}", $"{maxPulse:F0}")
                            .Replace("{1}", $"{maxTemp:F1}"));
                    }

                    if (riskLevel == "LOW" && criticals.Length == 0 && fallCount == 0)
                        conclusionParts.Add(T("export.conclusion.stable"));
                    else if (riskLevel == "HIGH")
                        conclusionParts.Add(T("export.conclusion.highRisk"));
                    else
                        conclusionParts.Add(T("export.conclusion.monitoring"));

                    // Specific findings
                    if (criticals.Length > 0)
                        conclusionParts.Add(T("export.conclusion.criticalSummary").Replace("{0}", $"{criticals.Length}"));
                    if (alerts.Length > 0)
                        conclusionParts.Add(T("export.conclusion.alertSummary").Replace("{0}", $"{alerts.Length}"));
                    if (fallCount > 0)
                        conclusionParts.Add(T("export.conclusion.fallSummary").Replace("{0}", $"{fallCount}"));

                    // Peak values summary
                    if (maxPulse > Person.MaxHeartRate || maxTemp > Person.MaxTemperature || minSpo2 < 95)
                    {
                        conclusionParts.Add(T("export.conclusion.peakValues")
                            .Replace("{0}", $"{maxPulse:F0}")
                            .Replace("{1}", $"{maxTemp:F1}")
                            .Replace("{2}", $"{minSpo2:F1}"));
                    }

                    conclusionParts.Add(T("export.conclusion.recommendation"));
                }

                // Asamblează obiectul anonim complet cu toate textele traduse și datele calculate,
                // trimis către componenta JS de generare PDF (pdfExport.generateMedicalReport).
                var pdfData = new
                {
                    // Header
                    reportTitle = T("export.reportTitle"),
                    generatedAt = $"{T("export.generatedAt")} {DateTime.Now:dd MMM yyyy, HH:mm}",
                    // 1. Patient info
                    patientSectionTitle = T("export.patientInfo"),
                    firstNameLabel = T("selected.firstName"),
                    patientFirstName = Person.FirstName,
                    lastNameLabel = T("selected.lastName"),
                    patientLastName = Person.LastName,
                    ageLabel = T("export.age"),
                    patientAge = Person.Age > 0 ? $"{Person.Age} {T("selected.years")}" : "-",
                    addressLabel = T("export.address"),
                    address = Person.Location,
                    // 2. Period
                    periodSectionTitle = T("export.selectedPeriod"),
                    period = periodLabel,
                    // 3. Summary
                    summarySectionTitle = T("export.summary"),
                    summary,
                    // Headers for summary table
                    hMetric = T("export.metric"),
                    hAvg = T("export.avg"),
                    hMin = T("export.min"),
                    hMax = T("export.max"),
                    hStdDev = T("export.stdDev"),
                    // 4. Weekly breakdown
                    weeklySectionTitle = T("export.weeklyBreakdown"),
                    weeklyBreakdown,
                    // 5. Daily breakdown
                    dailySectionTitle = T("export.dailyBreakdown"),
                    dailyBreakdown,
                    // 6. Alerts
                    alertsSectionTitle = T("export.alerts"),
                    alerts,
                    // 7. Critical events
                    criticalsSectionTitle = T("export.criticalEvents"),
                    criticals,
                    // 8. Raw data
                    rawDataSectionTitle = T("export.rawData"),
                    rawData,
                    // 9. Interpretation (severity-aware)
                    interpretationSectionTitle = T("export.interpretation"),
                    interpretations = interpretationItems.ToArray(),
                    // Risk score + breakdown
                    riskScore,
                    riskLevel,
                    riskScoreLabel = T("export.riskScore"),
                    riskBreakdown = riskBreakdown.ToArray(),
                    riskBreakdownTitle = T("export.riskBreakdown"),
                    // Data confidence
                    dataConfidence,
                    dataConfidenceNote,
                    dataConfidenceLabel = T("export.dataConfidence"),
                    // Top concerns
                    topConcerns = topConcerns.ToArray(),
                    topConcernsTitle = T("export.topConcerns"),
                    // 10. Conclusion
                    conclusionSectionTitle = T("export.conclusion"),
                    conclusion = conclusionParts.ToArray(),
                    hDate = T("export.date"),
                    hPulse = T("selected.heartRate"),
                    hTemp = T("selected.temperature"),
                    hSpo2 = "SpO2",
                    hActivity = T("export.activity"),
                    hFall = T("export.fall"),
                    hReason = T("export.reason"),
                    hWeek = T("export.week"),
                    hCount = T("export.count"),
                    patientName = Person.Name,
                    // Footer
                    footerDisclaimer = T("export.disclaimer")
                };

                await JSRuntime.InvokeVoidAsync("pdfExport.generateMedicalReport", pdfData);
                _showExportModal = false;
            }
            catch (Exception ex)
            {
                // Eroare de generare — afișată direct utilizatorului via alert() din browser.
                await JSRuntime.InvokeVoidAsync("alert", $"Export failed: {ex.Message}");
            }
            finally
            {
                _isExporting = false;
                StateHasChanged();
            }
        }

        // Calculează deviația standard (eșantion, n-1) a unei serii de valori — folosită pentru
        // rapoartele PDF, pentru a indica variabilitatea măsurătorilor în jurul mediei.
        private static double StdDev(List<double> values)
        {
            if (values.Count <= 1) return 0;
            var avg = values.Average();
            var sumSq = values.Sum(v => (v - avg) * (v - avg));
            return Math.Sqrt(sumSq / (values.Count - 1));
        }

        // Deschide modalul de editare, reîncărcând datele complete și actuale ale persoanei direct
        // de la API (nu din cache-ul local Person), pentru a evita editarea unor date expirate.
        // Flux: MonitoredApiClient.GetMonitoredPersonByIdAsync(PersonId)
        //   → HTTP GET api/monitored/id/{PersonId}
        //   → MonitoredController.GetMonitoredPersonById()
        //   → IMonitoredService.GetMonitoredPersonByIdAsync() → DB
        //   → populează câmpurile _edit* din datele proaspete
        private async void OpenEditModal()
        {
            _editError = null;
            var monitored = await MonitoredApiClient.GetMonitoredPersonByIdAsync(PersonId);
            if (monitored != null)
            {
                _editFirstName = monitored.FirstName;
                _editLastName = monitored.LastName;
                _editGender = monitored.Gender;
                _editBirthdate = monitored.Birthdate;
                _editAddress = monitored.Address;
                _editDeviceSerial = monitored.DeviceSerialNumber;
                _editMinHr = monitored.MinHeartRate;
                _editMaxHr = monitored.MaxHeartRate;
                _editMinTemp = monitored.MinTemperature;
                _editMaxTemp = monitored.MaxTemperature;
                _editMinSpO2 = monitored.MinSpO2;
                _editMaxSpO2 = monitored.MaxSpO2;
                _editUpdateFrequency = monitored.UpdateFrequency;
                _editDataRetentionDays = monitored.DataRetentionDays;
                _editArchiveRetentionDays = monitored.ArchiveRetentionDays;
            }
            _showEditModal = true;
            StateHasChanged();
        }

        private void CloseEditModal()
        {
            _showEditModal = false;
            _editError = null;
        }

        // Validează și salvează modificările din formularul de editare (date personale, device,
        // praguri vitale, frecvență de actualizare, retenție date/arhivă) către API.
        // Flux: MonitoredApiClient.UpdateMonitoredPersonAsync(PersonId, dto)
        //   → HTTP PUT api/monitored/update/{PersonId} { FirstName, LastName, DeviceSerialNumber, MinHR, MaxHR, ... }
        //   → MonitoredController.UpdateMonitoredPerson()
        //   → IMonitoredService.UpdateMonitoredPersonAsync() → IMonitoredRepository → DB
        //   → la succes: reîncarcă datele persoanei via LoadPersonDataAsync()
        private async Task SaveEditAsync()
        {
            _editError = null;
            // Validare minimă client-side înainte de a trimite cererea — câmpuri obligatorii.
            if (string.IsNullOrWhiteSpace(_editFirstName) || string.IsNullOrWhiteSpace(_editLastName))
            {
                _editError = "First name and last name are required.";
                return;
            }
            if (string.IsNullOrWhiteSpace(_editDeviceSerial))
            {
                _editError = "Device serial number is required.";
                return;
            }

            _isSaving = true;
            try
            {
                var dto = new MonitorUpdateRequestDTO
                {
                    FirstName = _editFirstName.Trim(),
                    LastName = _editLastName.Trim(),
                    Gender = _editGender,
                    Birthdate = _editBirthdate,
                    Address = _editAddress?.Trim() ?? "",
                    DeviceSerialNumber = _editDeviceSerial.Trim(),
                    MinHeartRate = _editMinHr,
                    MaxHeartRate = _editMaxHr,
                    MinTemperature = _editMinTemp,
                    MaxTemperature = _editMaxTemp,
                    MinSpO2 = _editMinSpO2,
                    MaxSpO2 = _editMaxSpO2,
                    UpdateFrequency = _editUpdateFrequency,
                    DataRetentionDays = _editDataRetentionDays,
                    ArchiveRetentionDays = _editArchiveRetentionDays
                };

                var success = await MonitoredApiClient.UpdateMonitoredPersonAsync(PersonId, dto);
                if (success)
                {
                    _showEditModal = false;
                    await LoadPersonDataAsync();
                    StateHasChanged();
                }
                else
                {
                    // Eroarea cea mai probabilă pe acest endpoint este un serial de device duplicat
                    // (unic per device în sistem), de aceea mesajul de eroare o menționează explicit.
                    _editError = "Failed to update. The device serial number may already be in use.";
                }
            }
            catch (Exception ex)
            {
                _editError = $"Error: {ex.Message}";
            }
            finally
            {
                _isSaving = false;
            }
        }

        // Deschide modalul de confirmare pentru ștergerea/arhivarea persoanei monitorizate.
        private void OpenDeleteModal()
        {
            _deleteError = null;
            _showDeleteModal = true;
        }

        private void CloseDeleteModal()
        {
            _showDeleteModal = false;
            _deleteError = null;
        }

        // Execută operația de ștergere a persoanei monitorizate (server-side este, de regulă,
        // un soft-delete — DeletedAt setat — nu o eliminare definitivă imediată; ștergerea fizică
        // se face ulterior conform politicii de retenție de 7 zile din RetentionCleanupService).
        // La succes, utilizatorul este redirecționat la lista de persoane monitorizate.
        // Flux: MonitoredApiClient.RemoveMonitoredAsync(PersonId)
        //   → HTTP DELETE api/monitored/{PersonId}/remove
        //   → MonitoredController.RemoveMonitoredPerson()
        //   → dacă ultimul proprietar: IMonitoredService.SoftDeleteMonitoredPersonAsync() → DB (DeletedAt setat)
        //   → dacă există co-proprietari: IUserMonitoredService.RemoveUserMonitoredLinkAsync() → DB (doar legătura)
        //   → returnează RemoveMonitoredResult { WasLastOwner, Message }
        private async Task ExecuteDeleteAsync()
        {
            _isDeleting = true;
            _deleteError = null;
            try
            {
                var result = await MonitoredApiClient.RemoveMonitoredAsync(PersonId);
                if (result == null)
                {
                    _deleteError = "Operațiunea a eșuat. Încearcă din nou.";
                    return;
                }
                _showDeleteModal = false;
                NavigationManager.NavigateTo("/monitored");
            }
            catch (Exception ex)
            {
                _deleteError = $"Eroare: {ex.Message}";
            }
            finally
            {
                _isDeleting = false;
            }
        }

        // Model de date pentru afișarea în UI a persoanei monitorizate — combină datele de profil cu
        // valorile vitale curente, statusul calculat și informațiile de stare (activă/arhivată/ștearsă).
        public class PersonDetail
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string FirstName { get; set; } = string.Empty;
            public string LastName { get; set; } = string.Empty;
            public int Age { get; set; }
            public int HeartRate { get; set; }
            public int SpO2 { get; set; }
            public double Temperature { get; set; }
            public string GPS { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public string LastUpdate { get; set; } = string.Empty;
            public string Location { get; set; } = string.Empty;
            public string DeviceSerial { get; set; } = string.Empty;
            public int MinHeartRate { get; set; } = 60;
            public int MaxHeartRate { get; set; } = 100;
            public double MinTemperature { get; set; } = 36.0;
            public double MaxTemperature { get; set; } = 37.5;
            public int MinSpO2 { get; set; } = 95;
            public int MaxSpO2 { get; set; } = 100;
            public int UpdateFrequency { get; set; } = 30;
            public bool IsArchived { get; set; }
            public DateTime? ArchivedAt { get; set; }
            public DateTime? DeletedAt { get; set; }
            // Persoana este considerată "doar-citire" în UI dacă a fost arhivată sau șters (soft-delete) —
            // în acest caz pagina ar trebui să blocheze editări/acțiuni active (monitorizare live, praguri etc.).
            public bool IsReadOnly => IsArchived || DeletedAt.HasValue;
            public int? DataRetentionDays { get; set; }
            public int? ArchiveRetentionDays { get; set; }
        }

        // Un singur punct de date pentru grafic — poate reprezenta fie o măsurătoare individuală
        // (vizualizare zilnică, cu XFraction explicit pe baza orei), fie o medie zilnică/săptămânală
        // (vizualizare săptămânală, poziționată pe index).
        public class ChartDataPoint
        {
            public string Day { get; set; } = string.Empty;
            public int Value { get; set; }
            public double ActualValue { get; set; }
            public bool HasData { get; set; } = true;
            public double XFraction { get; set; } = -1; // -1 = index-based; 0.0-1.0 = explicit
        }

        // Punct pregătit pentru tooltip JS: coordonatele afișate (X, Y), valoarea/eticheta de arătat,
        // și zona de "hit testing" (HitX, HitWidth) pe care trebuie să se declanșeze hover-ul.
        public record TooltipPoint(double X, double Y, double Value, string Label, double HitX, double HitWidth);

        // Similar cu ComputePointsWithRange, dar produce TooltipPoint-uri (cu zone de hover calculate)
        // în loc de simple coordonate (X, Y) — folosit pentru interacțiunea cu graficele (chartTooltip.js).
        private List<TooltipPoint> ComputeTooltipData(
            List<ChartDataPoint> data, double fixedMin, double fixedMax)
        {
            if (data == null || data.Count == 0) return new();

            const double paddingLeft = 90;
            const double paddingRight = 15;
            const double paddingTop = 15;
            var usableWidth = ChartViewBoxWidth - paddingLeft - paddingRight;
            var usableHeight = 145.0;

            double minVal = fixedMin;
            double maxVal = fixedMax;
            var range = maxVal - minVal;
            if (range < 0.001) range = 10;

            int n = data.Count;
            var points = data
                .Select((d, i) => (
                    HasData: d.HasData,
                    Value: d.ActualValue,
                    Day: d.Day,
                    X: paddingLeft + (d.XFraction >= 0
                        ? d.XFraction * usableWidth
                        : (n <= 1 ? usableWidth / 2.0 : (double)i / (n - 1) * usableWidth)),
                    Y: paddingTop + usableHeight * (1.0 - Math.Clamp((d.ActualValue - minVal) / range, 0.0, 1.0))
                ))
                .Where(p => p.HasData)
                .OrderBy(p => p.X)
                .ToList();

            // Adjust X positions to avoid overlapping tooltip targets / dots
            // (aceeași logică de separare folosită la desenarea punctelor, aplicată aici pentru zonele de hover)
            var minX = paddingLeft;
            var maxX = paddingLeft + usableWidth;
            double spacing = CurrentChartView == ChartViewMode.Daily ? 12.0 : 8.0;
            var adjustedXY = SpreadCloseXs(points.Select(p => (p.X, p.Y)).ToList(), minX, maxX, spacing);

            if (points.Count == 0) return new();

            var result = new List<TooltipPoint>();
            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                var adj = adjustedXY[i];
                // Zona de hover a unui punct se extinde pe la mijlocul distanței dintre el și vecini
                // (sau până la marginea graficului pentru primul/ultimul punct), pentru ca tot spațiul
                // orizontal al graficului să fie acoperit de o zonă de hit testing, nu doar cercul punctului.
                double left = i == 0 ? paddingLeft : (adjustedXY[i - 1].X + adj.X) / 2.0;
                double right = i == points.Count - 1 ? paddingLeft + usableWidth : (adj.X + adjustedXY[i + 1].X) / 2.0;
                result.Add(new TooltipPoint(adj.X, adj.Y, p.Value, p.Day, left, right - left));
            }

            return result;
        }

        // Model simplu pentru o alertă afișată în lista "alerte recente" a paginii.
        public class Alert
        {
            public string Severity { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string Time { get; set; } = string.Empty;
        }

        // Model simplu pentru o măsurătoare afișată în lista "activitate recentă" a paginii.
        public class Measurement
        {
            public string Icon { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
            public string Time { get; set; } = string.Empty;
        }
    }
}
