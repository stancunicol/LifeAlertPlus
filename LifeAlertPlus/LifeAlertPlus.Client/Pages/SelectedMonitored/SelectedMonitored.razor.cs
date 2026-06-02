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
    public partial class SelectedMonitored : ComponentBase, IAsyncDisposable
    {
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

        private string T(string key) => Lang.T(key);

        private ElementReference _hrSvgRef;
        private ElementReference _tempSvgRef;
        private ElementReference _hrScrollRef;
        private ElementReference _tempScrollRef;
        private ElementReference _mapRef;
        private bool _tooltipsInitialized;
        private bool _mapInitialized;
        private bool _scrollSyncInitialized;

        private DayOfWeek _firstDayOfWeek = DayOfWeek.Monday;
        private PersonDetail? Person { get; set; }
        private bool IsLoading { get; set; } = true;
        private LifeAlertPlus.Shared.DTOs.Responses.ESP.ESPDataResponseDTO? _espData;
        private string? LoadError { get; set; }

        private List<ChartDataPoint> HeartRateHistory { get; set; } = new();
        private List<ChartDataPoint> TemperatureHistory { get; set; } = new();
        private List<ChartDataPoint> SpO2History { get; set; } = new();
        private List<(double X, double Y)> HeartRatePoints { get; set; } = new();
        private List<(double X, double Y)> TemperaturePoints { get; set; } = new();
        private List<(double X, double Y)> SpO2Points { get; set; } = new();
        private List<TooltipPoint> HrTooltipData { get; set; } = new();
        private List<TooltipPoint> TempTooltipData { get; set; } = new();
        private List<TooltipPoint> SpO2TooltipData { get; set; } = new();
        private List<Alert> RecentAlerts { get; set; } = new();
        private List<Measurement> RecentMeasurements { get; set; } = new();
        private AIPredictionResponseDTO? AIPrediction { get; set; }
        private bool AIPredictionLoading { get; set; }
        private bool _showProbabilities = false;
        private LifeAlertPlus.Shared.DTOs.Responses.Monitoring.TrendPredictionResponseDTO? TrendPredictions { get; set; }
        private bool TrendPredictionsLoading { get; set; }
        private ActivityProfileResponseDTO? ActivityProfile { get; set; }
        private bool ActivityProfileLoading { get; set; }
        private string UserFullName = "";
        private string ProfilePictureUrl = "";
        private ChartViewMode CurrentChartView { get; set; } = ChartViewMode.Daily;
        private int _weekOffset = 0; // 0 = current week, -1 = previous week, etc.
        private string _chartWeekLabel = "";
        private bool _hasPrevWeekData = false;
        private int _dayOffset = 0; // 0 = today, -1 = yesterday, etc.
        private string _chartDayLabel = "";
        private bool _hasPrevDayData = false;
        private System.Threading.Timer? _refreshTimer;
        private bool _disposed = false;

        // ── False-alarm feedback ──────────────────────────────────────────────
        private LifeAlertPlus.Shared.DTOs.Responses.Notification.PendingFeedbackDTO? _pendingFeedback;
        private bool _feedbackSubmitting;

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

        private double ChartViewBoxWidth => ChartViewBoxBaseWidth * _chartZoom;
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

        private async Task ZoomChartIn()
        {
            if (_chartZoom >= ChartMaxZoom) return;
            _chartZoom = Math.Min(ChartMaxZoom, _chartZoom + ChartZoomStep);
            RecomputeChartPoints();
            await InvokeAsync(StateHasChanged);
            await InitTooltipsAsync();
        }

        private async Task ZoomChartOut()
        {
            if (_chartZoom <= ChartMinZoom) return;
            _chartZoom = Math.Max(ChartMinZoom, _chartZoom - ChartZoomStep);
            RecomputeChartPoints();
            await InvokeAsync(StateHasChanged);
            await InitTooltipsAsync();
        }

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

        private void OnPushNotificationReceived(string message, string severity)
        {
            if (_disposed)
                return;

            _ = InvokeAsync(async () =>
            {
                await RefreshDataAsync();
            });
        }

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
        private int _userMinHr = 60;
        private int _userMaxHr = 100;
        private double _userMinTemp = 36.0;
        private double _userMaxTemp = 37.5;
        private int _userUpdateFrequency = 30;

        // Edit modal state
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

        private static SelectedMonitored? _instance;

        private enum ChartViewMode
        {
            Daily,
            Weekly
        }

        private async Task LoadPersonDataAsync()
        {
            IsLoading = true;
            LoadError = null;

            try
            {
                var monitored = await MonitoredApiClient.GetMonitoredPersonByIdAsync(PersonId);
                if (monitored == null)
                {
                    Person = null;
                    LoadError = "Monitored person not found.";
                    IsLoading = false;
                    return;
                }

                // Get ESP data (stored as field so the device diagnostics card can access it)
                var espData = await MonitoredApiClient.GetEspDataAsync(monitored.DeviceSerialNumber);
                _espData = espData;
                
                int heartRate = 0;
                int spO2 = 0;
                double temperature = 0;
                string gps = "No data";
                string status = "OK";

                if (espData?.IsAvailable == true)
                {
                    heartRate = espData.Bpm ?? 0;
                    spO2 = espData.Spo2 ?? 0;
                    
                    temperature = espData.Temperature ?? 0;
                    gps = espData.Neo6m ?? "No data";

                    // Determine status using per-person ranges (fallback to user defaults)
                    int effectiveMinHr = monitored.MinHeartRate ?? _userMinHr;
                    int effectiveMaxHr = monitored.MaxHeartRate ?? _userMaxHr;
                    double effectiveMinTemp = monitored.MinTemperature ?? _userMinTemp;
                    double effectiveMaxTemp = monitored.MaxTemperature ?? _userMaxTemp;

                    if ((heartRate > 0 && (heartRate > effectiveMaxHr || heartRate < effectiveMinHr - 10)) ||
                        (spO2 > 0 && spO2 < 90) ||
                        (temperature > 0 && (temperature > effectiveMaxTemp + 0.5 || temperature < effectiveMinTemp - 0.5)))
                    {
                        status = "Critical";
                    }
                    else if ((heartRate > 0 && (heartRate > effectiveMaxHr - 10 || heartRate < effectiveMinHr)) ||
                             (spO2 > 0 && spO2 < 95) ||
                             (temperature > 0 && (temperature > effectiveMaxTemp || temperature < effectiveMinTemp)))
                    {
                        status = "Warning";
                    }
                }
                else
                {
                    status = "Offline";
                }

                // Get last measurement time
                var measurements = await MeasurementApiClient.GetMeasurementsByMonitoredIdAsync(monitored.Id, 1, 1);
                var lastMeasurement = measurements?.FirstOrDefault();
                string lastUpdate = lastMeasurement != null 
                    ? lastMeasurement.CreatedAt.ToLocalTime().ToString("MMMM dd, yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture)
                    : "No data";

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
                    DataRetentionDays = monitored.DataRetentionDays,
                    ArchiveRetentionDays = monitored.ArchiveRetentionDays
                };

                // Skip live/predictive features for archived persons — they have no active
                // monitoring, so AI predictions, trend predictions and activity profile
                // would always show stale or empty data.
                if (!monitored.IsArchived)
                {
                    if (espData?.IsAvailable == true)
                        _ = LoadAIPredictionAsync(espData);
                    _ = LoadTrendPredictionsAsync(PersonId);
                    _ = LoadActivityProfileAsync(PersonId);
                }
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

        private async Task LoadAIPredictionAsync(LifeAlertPlus.Shared.DTOs.Responses.ESP.ESPDataResponseDTO espData)
        {
            var request = new AIPredictionRequestDTO
            {
                MonitoredId = PersonId,
                Pulse = espData.Max30100 != null && espData.Max30100.Count >= 1 ? espData.Max30100[0] : 0,
                Temperature = espData.Temperature ?? 0,
                Spo2 = espData.Max30100 != null && espData.Max30100.Count >= 2 ? espData.Max30100[1] : 97.0,
                AccelX = espData.Mpu6050 != null && espData.Mpu6050.Count >= 1 ? espData.Mpu6050[0] : 0,
                AccelY = espData.Mpu6050 != null && espData.Mpu6050.Count >= 2 ? espData.Mpu6050[1] : 0,
                AccelZ = espData.Mpu6050 != null && espData.Mpu6050.Count >= 3 ? espData.Mpu6050[2] : 0,
                GyroX = espData.Gyro != null && espData.Gyro.Count >= 1 ? espData.Gyro[0] : 0,
                GyroY = espData.Gyro != null && espData.Gyro.Count >= 2 ? espData.Gyro[1] : 0,
                GyroZ = espData.Gyro != null && espData.Gyro.Count >= 3 ? espData.Gyro[2] : 0,
            };
            await RunAIPredictionAsync(request);
        }

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

        private List<string> _conditions = new();
        private List<string> _editConditions = new();
        private bool _conditionsLoading;
        private bool _showConditionsModal;
        private bool _isSavingConditions;

        private record ConditionGroupDef(string CategoryKey, string Icon, string ColorClass, List<string> Keys);

        private static readonly List<ConditionGroupDef> ConditionGroups = new()
        {
            new("conditions.cardio",       "❤️",  "cardio",      new() { "hypertension", "arrhythmia", "heart_failure", "mi_risk" }),
            new("conditions.respiratory",  "🫁",  "respiratory", new() { "asthma", "copd" }),
            new("conditions.neuro",        "🧠",  "neuro",       new() { "parkinson", "epilepsy" }),
            new("conditions.metabolic",    "🔬",  "metabolic",   new() { "diabetes" }),
        };

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
                StateHasChanged();
            }
        }

        private void OpenConditionsModal()
        {
            _editConditions = new List<string>(_conditions);
            CalculateConditionThresholds();
            _showConditionsModal = true;
        }

        private void CloseConditionsModal() => _showConditionsModal = false;

        private void ToggleCondition(string key, bool isChecked)
        {
            if (isChecked && !_editConditions.Contains(key))
                _editConditions.Add(key);
            else if (!isChecked)
                _editConditions.Remove(key);
            CalculateConditionThresholds();
        }

        // Client-side mirror of ConditionThresholdAdjuster.Calculate
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
                if (p.MaxTemp < maxTemp) maxTemp = p.MaxTemp;
                if (p.MinSpO2 < minSpO2) minSpO2 = p.MinSpO2;
            }

            _previewMinHr   = minHr;
            _previewMaxHr   = maxHr;
            _previewMinTemp = minTemp;
            _previewMaxTemp = maxTemp;
            _previewMinSpO2 = minSpO2;
            _previewMaxSpO2 = maxSpO2;
        }

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

        private sealed class ConditionThresholdResponseDTO
        {
            public int? MinHeartRate   { get; set; }
            public int? MaxHeartRate   { get; set; }
            public double? MinTemperature { get; set; }
            public double? MaxTemperature { get; set; }
            public int? MinSpO2        { get; set; }
            public int? MaxSpO2        { get; set; }
        }

        private static string GetActivitySlotClass(string label) => label switch
        {
            "Somn" => "slot-sleep",
            "Activ" => "slot-active",
            "Moderat activ" => "slot-moderate",
            "Inactiv / Odihnă" => "slot-rest",
            _ => "slot-nodata"
        };

        private string GetSlotTooltip(HourlyProfileDTO? slot, int hour)
        {
            if (slot == null || slot.DataPoints < 10)
                return $"{hour:00}:00 – {T("selected.activityNoData")}";
            return $"{hour:00}:00 | {slot.Label} | {T("selected.slotAvgPulse")}: {slot.AveragePulse:F0} bpm | {T("selected.slotMovement")}: {slot.MovementRate:P0} | {T("selected.activitySleep")}: {slot.SleepProbability:P0}";
        }

        private List<ChartDataPoint> HeartRateHistoryFiltered =>
            HeartRateHistory.Where(d => d.HasData).ToList();
        private List<ChartDataPoint> TemperatureHistoryFiltered =>
            TemperatureHistory.Where(d => d.HasData).ToList();

        private async Task LoadChartDataAsync()
        {
            try
            {
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

                HeartRatePoints = ComputePointsWithRange(HeartRateHistory, 40, 140);
                TemperaturePoints = ComputePointsWithRange(TemperatureHistory, 35, 39);
                HrTooltipData = ComputeTooltipData(HeartRateHistory, 40, 120);
                TempTooltipData = ComputeTooltipData(TemperatureHistory, 35, 39);

                // Ensure UI updates immediately after data is loaded so points appear
                await InvokeAsync(StateHasChanged);
            }
            catch
            {
                LoadEmptyChartData();
            }
        }

        private void LoadDailyChartData(List<MeasurementResponseDTO> measurements)
        {
            var targetDay = DateTime.Now.Date.AddDays(_dayOffset);

            // Set day label
            if (_dayOffset == 0)
                _chartDayLabel = targetDay.ToString("dddd, dd MMM yyyy");
            else
                _chartDayLabel = targetDay.ToString("dddd, dd MMM yyyy");

            // Check if previous day has data
            var prevDay = targetDay.AddDays(-1);
            _hasPrevDayData = measurements.Any(m => m.CreatedAt.ToLocalTime().Date == prevDay);

            var todayMs = measurements
                .Where(m => m.CreatedAt.ToLocalTime().Date == targetDay)
                .OrderBy(m => m.CreatedAt)
                .ToList();

            if (!todayMs.Any()) { LoadEmptyChartData(); return; }

            // One point per measurement — the wide, zoomable chart gives them enough room
            // to be readable as distinct dots, and each one is hover-tooltip addressable.
            // Sensor-failure readings (= 0) are excluded per metric.
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

        private static List<T> SampleEvenly<T>(List<T> source, int count)
        {
            var result = new List<T>(count);
            double step = (double)(source.Count - 1) / (count - 1);
            for (int i = 0; i < count; i++)
                result.Add(source[(int)Math.Round(i * step)]);
            return result;
        }

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

        private async Task GoToPreviousWeek()
        {
            if (!_hasPrevWeekData) return;
            _weekOffset--;
            await ReloadChartAsync();
        }

        private async Task GoToNextWeek()
        {
            if (_weekOffset >= 0) return;
            _weekOffset++;
            await ReloadChartAsync();
        }

        private async Task GoToPreviousDay()
        {
            if (!_hasPrevDayData) return;
            _dayOffset--;
            await ReloadChartAsync();
        }

        private async Task GoToNextDay()
        {
            if (_dayOffset >= 0) return;
            _dayOffset++;
            await ReloadChartAsync();
        }

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

        private List<(double X, double Y)> ComputePoints(List<ChartDataPoint> data)
        {
            return ComputePointsWithRange(data, 0, 0);
        }

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
            var minX = paddingLeft;
            var maxX = paddingLeft + usableWidth;
            double spacing = CurrentChartView == ChartViewMode.Daily ? 12.0 : 8.0;
            return SpreadCloseXs(pts, minX, maxX, spacing);
        }

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
private static string F(double v) => v.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

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

        private void LoadEmptyChartData()
        {
            HeartRateHistory = new List<ChartDataPoint>();
            TemperatureHistory = new List<ChartDataPoint>();
            HeartRatePoints = new List<(double X, double Y)>();
            TemperaturePoints = new List<(double X, double Y)>();
            HrTooltipData = new List<TooltipPoint>();
            TempTooltipData = new List<TooltipPoint>();
        }

        // Spread close X positions so plotted circles don't visually overlap
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

                foreach (var m in measurements.Take(10))
                {
                    // Check for critical conditions
                    if (m.Pulse > 100 || m.Pulse < 50)
                    {
                        alerts.Add(new Alert
                        {
                            Severity = "Critical",
                            Title = m.Pulse > 100 ? "High Heart Rate" : "Low Heart Rate",
                            Description = $"Heart rate: {m.Pulse} bpm",
                            Time = GetTimeAgo(m.CreatedAt)
                        });
                    }

                    if (m.Temperature > 37.5)
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

                RecentAlerts = alerts.Take(5).ToList();
            }
            catch
            {
                RecentAlerts = new List<Alert>();
            }
        }

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

        private string GetTimeAgo(DateTime dateTime)
        {
            var timeSpan = DateTime.UtcNow - dateTime;

            if (timeSpan.TotalMinutes < 1)
                return "Just now";
            if (timeSpan.TotalHours < 1)
                return $"{(int)timeSpan.TotalMinutes}m ago";
            if (timeSpan.TotalDays < 1)
                return $"{(int)timeSpan.TotalHours}h ago";
            if (timeSpan.TotalDays < 7)
                return $"{(int)timeSpan.TotalDays}d ago";
            return dateTime.ToLocalTime().ToString("MMM dd", System.Globalization.CultureInfo.InvariantCulture);
        }

        private string GetInitials(string name)
        {
            var parts = name.Split(' ');
            if (parts.Length >= 2)
                return $"{parts[0][0]}{parts[1][0]}".ToUpper();
            return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpper();
        }

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

            await Task.WhenAll(LoadPersonDataAsync(), LoadChartDataAsync(), LoadRecentAlertsAsync(), LoadRecentMeasurementsAsync());

            PushService.OnNotificationReceived += OnPushNotificationReceived;
            MeasurementApiClient.OnMeasurementAdded += OnMeasurementAdded;

            _ = LoadPendingFeedbackAsync();

            // Start auto-refresh timer (uses user-configured update frequency)
            _refreshTimer = new System.Threading.Timer(_ => _ = RefreshDataAsync(), null, TimeSpan.FromSeconds(_userUpdateFrequency), TimeSpan.FromSeconds(_userUpdateFrequency));
        }

        private async Task RefreshDataAsync()
        {
            if (_disposed) return;

            // Drop overlapping refreshes: a single cascade already issues ~8 GETs.
            // Without this guard, push notifications + timer + measurement events
            // can stack and produce multiplied request bursts.
            if (System.Threading.Interlocked.Exchange(ref _refreshInFlight, 1) == 1)
                return;
            if (DateTime.UtcNow - _lastRefreshUtc < MinRefreshInterval)
            {
                System.Threading.Interlocked.Exchange(ref _refreshInFlight, 0);
                return;
            }

            try
            {
                await InvokeAsync(async () =>
                {
                    await Task.WhenAll(LoadPersonDataAsync(), LoadChartDataAsync(), LoadRecentAlertsAsync(), LoadRecentMeasurementsAsync());
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

                _tooltipsInitialized = HrTooltipData.Count > 0 || TempTooltipData.Count > 0;
            }
            catch
            {
                // JS interop may fail during prerender
            }
        }

        public async ValueTask DisposeAsync()
        {
            _disposed = true;
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
                await JSRuntime.InvokeVoidAsync("chartSync.detach", $"selected-{PersonId}");
            }
            catch { }
        }

        private string GetVitalStatus(int value, int min, int max)
        {
            if (min <= 0 || max <= 0) return "normal";
            if (value < min || value > max)
                return "warning";
            return "normal";
        }

        private string GetVitalStatusText(int value, int min, int max)
        {
            if (min <= 0 || max <= 0) return T("vital.normal");
            if (value < min)
                return T("vital.belowNormal");
            if (value > max)
                return T("vital.aboveNormal");
            return T("vital.normal");
        }

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

        private string GetSpO2Status(int spO2, int minSpO2 = 95)
        {
            int critSpO2 = Math.Max(70, minSpO2 - 5);
            if (spO2 > 0 && spO2 < critSpO2) return "critical";
            if (spO2 > 0 && spO2 < minSpO2)  return "warning";
            return "normal";
        }

        private string GetSpO2StatusText(int spO2, int minSpO2 = 95)
        {
            int critSpO2 = Math.Max(70, minSpO2 - 5);
            if (spO2 > 0 && spO2 < critSpO2) return T("vital.criticalLow");
            if (spO2 > 0 && spO2 < minSpO2)  return T("vital.belowNormal");
            return T("vital.normal");
        }

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

        private static string GetTrendMetricIcon(string metric) => metric switch
        {
            "temperature" => "🌡️",
            "pulse" => "❤️",
            "spo2" => "🩸",
            _ => "📊"
        };

        private static string FormatTrendRate(string metric, double ratePerMinute) => metric switch
        {
            "temperature" => $"{(ratePerMinute >= 0 ? "+" : "")}{ratePerMinute:F2} °C/min",
            "pulse" => $"{(ratePerMinute >= 0 ? "+" : "")}{ratePerMinute:F1} bpm/min",
            "spo2" => $"{(ratePerMinute >= 0 ? "+" : "")}{ratePerMinute:F2} %/min",
            _ => $"{ratePerMinute:F2}/min"
        };

        private static string FormatSecondsToThreshold(int seconds, string minLabel, string secLabel)
        {
            if (seconds >= 60)
                return $"~{seconds / 60} {minLabel} {seconds % 60} {secLabel}";
            return $"~{seconds} {secLabel}";
        }

        private string GetTrendLabel(LifeAlertPlus.Shared.DTOs.Responses.Monitoring.TrendPredictionItemDTO pred)
        {
            var key = $"trend.{pred.Metric}.{pred.Direction}.{pred.Severity}";
            var translated = T(key);
            return translated == key ? pred.Label : translated;
        }

        private string GetTrendThreshold(LifeAlertPlus.Shared.DTOs.Responses.Monitoring.TrendPredictionItemDTO pred)
        {
            var key = $"trend.threshold.{pred.Metric}.{pred.Direction}";
            var translated = T(key);
            return translated == key ? (pred.ThresholdDescription ?? string.Empty) : translated;
        }

        private void GoBack()
        {
            NavigationManager.NavigateTo("/monitored");
        }

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
                    if (filtered.Count >= 30 && totalDays >= 7)
                    { dataConfidence = "HIGH"; dataConfidenceNote = T("export.confidence.high").Replace("{0}", $"{filtered.Count}").Replace("{1}", $"{totalDays}"); }
                    else if (filtered.Count >= 14 && totalDays >= 3)
                    { dataConfidence = "MEDIUM"; dataConfidenceNote = T("export.confidence.medium").Replace("{0}", $"{filtered.Count}").Replace("{1}", $"{totalDays}"); }
                    else
                    { dataConfidence = "LOW"; dataConfidenceNote = T("export.confidence.low").Replace("{0}", $"{filtered.Count}").Replace("{1}", $"{totalDays}"); }

                    // ── Heart Rate ──
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

                    if (maxPulse - minPulse > 50)
                    {
                        interpretationItems.Add(new { text = T("export.interp.hrVariability").Replace("{0}", $"{minPulse:F0}").Replace("{1}", $"{maxPulse:F0}"), plain = T("export.plain.hrVariability"), severity = "medium" });
                        riskScore += 5;
                        riskBreakdown.Add(new { factor = T("export.risk.hrVariability"), points = 5 });
                    }

                    // ── Temperature ──
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
                    riskScore = Math.Min(riskScore, 100);
                    riskLevel = riskScore >= 60 ? "HIGH" : riskScore >= 30 ? "MEDIUM" : "LOW";

                    // ── Top Concerns (ranked by severity, max 3) ──
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
                await JSRuntime.InvokeVoidAsync("alert", $"Export failed: {ex.Message}");
            }
            finally
            {
                _isExporting = false;
                StateHasChanged();
            }
        }

        private static double StdDev(List<double> values)
        {
            if (values.Count <= 1) return 0;
            var avg = values.Average();
            var sumSq = values.Sum(v => (v - avg) * (v - avg));
            return Math.Sqrt(sumSq / (values.Count - 1));
        }

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

        private async Task SaveEditAsync()
        {
            _editError = null;
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
            public int? DataRetentionDays { get; set; }
            public int? ArchiveRetentionDays { get; set; }
        }

        public class ChartDataPoint
        {
            public string Day { get; set; } = string.Empty;
            public int Value { get; set; }
            public double ActualValue { get; set; }
            public bool HasData { get; set; } = true;
            public double XFraction { get; set; } = -1; // -1 = index-based; 0.0-1.0 = explicit
        }

        public record TooltipPoint(double X, double Y, double Value, string Label, double HitX, double HitWidth);

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
                double left = i == 0 ? paddingLeft : (adjustedXY[i - 1].X + adj.X) / 2.0;
                double right = i == points.Count - 1 ? paddingLeft + usableWidth : (adj.X + adjustedXY[i + 1].X) / 2.0;
                result.Add(new TooltipPoint(adj.X, adj.Y, p.Value, p.Day, left, right - left));
            }

            return result;
        }

        public class Alert
        {
            public string Severity { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string Time { get; set; } = string.Empty;
        }

        public class Measurement
        {
            public string Icon { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
            public string Time { get; set; } = string.Empty;
        }
    }
}
