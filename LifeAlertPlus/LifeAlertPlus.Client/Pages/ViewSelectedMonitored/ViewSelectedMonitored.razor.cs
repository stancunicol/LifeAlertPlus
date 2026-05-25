using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Globalization;
using LifeAlertPlus.Client.Services;
using LifeAlertPlus.Shared.DTOs.Responses.Measurement;
using LifeAlertPlus.Shared.DTOs.Requests.Monitored;

namespace LifeAlertPlus.Client.Pages.ViewSelectedMonitored
{
    public partial class ViewSelectedMonitored : ComponentBase, IAsyncDisposable
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
        private IJSRuntime JSRuntime { get; set; } = default!;

        [Inject]
        private LanguageService Lang { get; set; } = default!;

        private string T(string key) => Lang.T(key);

        private ElementReference _hrSvgRef;
        private ElementReference _tempSvgRef;
        private ElementReference _mapRef;
        private bool _tooltipsInitialized;
        private bool _mapInitialized;

        private DayOfWeek _firstDayOfWeek = DayOfWeek.Monday;
        private PersonDetail? Person { get; set; }
        private bool IsLoading { get; set; } = true;
        private string? LoadError { get; set; }

        private List<ChartDataPoint> HeartRateHistory { get; set; } = new();
        private List<ChartDataPoint> TemperatureHistory { get; set; } = new();
        private List<(double X, double Y)> HeartRatePoints { get; set; } = new();
        private List<(double X, double Y)> TemperaturePoints { get; set; } = new();
        private List<TooltipPoint> HrTooltipData { get; set; } = new();
        private List<TooltipPoint> TempTooltipData { get; set; } = new();
        private List<Alert> RecentAlerts { get; set; } = new();
        private List<Measurement> RecentMeasurements { get; set; } = new();
        private string UserFullName = "";
        private string ProfilePictureUrl = "";
        private ChartViewMode CurrentChartView { get; set; } = ChartViewMode.Daily;
        private System.Threading.Timer? _refreshTimer;
        private bool _disposed = false;

        // User global vital range fallbacks
        private int _userMinHr = 60;
        private int _userMaxHr = 100;
        private double _userMinTemp = 36.0;
        private double _userMaxTemp = 37.5;
        private int _userUpdateFrequency = 30;

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

                // Get ESP data
                var espData = await MonitoredApiClient.GetEspDataAsync(monitored.DeviceSerialNumber);
                
                int heartRate = 0;
                int spO2 = 0;
                double temperature = 0;
                string gps = "No data";
                string status = "OK";

                if (espData?.IsAvailable == true)
                {
                    if (espData.Max30100 != null && espData.Max30100.Count >= 2)
                    {
                        heartRate = espData.Max30100.ElementAtOrDefault(0);
                        spO2 = espData.Max30100.ElementAtOrDefault(1);
                    }
                    
                    temperature = espData.Temperature ?? 0;
                    gps = espData.Neo6m ?? "No data";

                    // Determine status using per-person ranges (fallback to user defaults)
                    int effectiveMinHr = monitored.MinHeartRate ?? _userMinHr;
                    int effectiveMaxHr = monitored.MaxHeartRate ?? _userMaxHr;
                    double effectiveMinTemp = monitored.MinTemperature ?? _userMinTemp;
                    double effectiveMaxTemp = monitored.MaxTemperature ?? _userMaxTemp;

                    if (heartRate > effectiveMaxHr || heartRate < effectiveMinHr - 10 || spO2 < 90 || temperature > effectiveMaxTemp + 0.5 || temperature < effectiveMinTemp - 0.5)
                    {
                        status = "Critical";
                    }
                    else if (heartRate > effectiveMaxHr - 10 || heartRate < effectiveMinHr || spO2 < 95 || temperature > effectiveMaxTemp || temperature < effectiveMinTemp)
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
                    UpdateFrequency = monitored.UpdateFrequency ?? _userUpdateFrequency
                };

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

                HeartRatePoints = ComputePointsWithRange(HeartRateHistory, 40, 120);
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
            var today = DateTime.Now.Date;

            // For daily view we want to show every measurement of the day (no aggregation by buckets).
            var todayMs = measurements
                .Where(m => m.CreatedAt.ToLocalTime().Date == today)
                .OrderBy(m => m.CreatedAt)
                .ToList();

            if (!todayMs.Any()) { LoadEmptyChartData(); return; }

            HeartRateHistory = todayMs.Select(m => new ChartDataPoint
            {
                Day = m.CreatedAt.ToLocalTime().ToString("HH:mm"),
                ActualValue = m.Pulse,
                HasData = true,
                XFraction = m.CreatedAt.ToLocalTime().TimeOfDay.TotalHours / 24.0
            }).ToList();

            TemperatureHistory = todayMs.Select(m => new ChartDataPoint
            {
                Day = m.CreatedAt.ToLocalTime().ToString("HH:mm"),
                ActualValue = m.Temperature,
                HasData = true,
                XFraction = m.CreatedAt.ToLocalTime().TimeOfDay.TotalHours / 24.0
            }).ToList();
        }

        private void LoadWeeklyChartData(List<MeasurementResponseDTO> measurements)
        {
            var today = DateTime.Now.Date;

            // Find the start of the current week based on user's preferred first day
            int diff = ((int)today.DayOfWeek - (int)_firstDayOfWeek + 7) % 7;
            var weekStart = today.AddDays(-diff);
            var days = Enumerable.Range(0, 7).Select(i => weekStart.AddDays(i)).ToList();

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
        }

        private async Task SwitchChartView(ChartViewMode mode)
        {
            CurrentChartView = mode;
            HeartRateHistory = new List<ChartDataPoint>();
            TemperatureHistory = new List<ChartDataPoint>();
            HeartRatePoints = new List<(double X, double Y)>();
            TemperaturePoints = new List<(double X, double Y)>();
            HrTooltipData = new List<TooltipPoint>();
            TempTooltipData = new List<TooltipPoint>();
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
            var usableWidth = 800 - paddingLeft - paddingRight;  // 695
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
            var usableWidth = 800 - paddingLeft - paddingRight;

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

        private string GenerateSmoothPath(List<(double X, double Y)> pts)
        {
            if (pts == null || pts.Count == 0) return "";
            if (pts.Count == 1)
            {
                return $"M {F(pts[0].X)} {F(pts[0].Y)} L {F(pts[0].X + 1.0)} {F(pts[0].Y)}";
            }

            int n = pts.Count;
            if (n == 2)
                return $"M {F(pts[0].X)} {F(pts[0].Y)} L {F(pts[1].X)} {F(pts[1].Y)}";

            var dx = new double[n - 1];
            var dy = new double[n - 1];
            var slopes = new double[n - 1];
            for (int i = 0; i < n - 1; i++)
            {
                dx[i] = pts[i + 1].X - pts[i].X;
                dy[i] = pts[i + 1].Y - pts[i].Y;
                slopes[i] = dx[i] < 1e-10 ? 0 : dy[i] / dx[i];
            }

            var m = new double[n];
            m[0] = slopes[0];
            m[n - 1] = slopes[n - 2];
            for (int i = 1; i < n - 1; i++)
            {
                if (slopes[i - 1] * slopes[i] <= 0)
                    m[i] = 0;
                else
                    m[i] = (slopes[i - 1] + slopes[i]) / 2.0;
            }

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

            var path = new System.Text.StringBuilder();
            path.Append($"M {F(pts[0].X)} {F(pts[0].Y)}");

            for (int i = 0; i < n - 1; i++)
            {
                double seg = dx[i] / 3.0;
                double cp1x = pts[i].X + seg;
                double cp1y = pts[i].Y + m[i] * seg;
                double cp2x = pts[i + 1].X - seg;
                double cp2y = pts[i + 1].Y + m[i + 1] * -seg + 0; // keep similar math

                double cp2y_fixed = pts[i + 1].Y - m[i + 1] * seg;

                path.Append($" C {F(cp1x)} {F(cp1y)}, {F(cp2x)} {F(cp2y_fixed)}, {F(pts[i + 1].X)} {F(pts[i + 1].Y)}");
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

        private static List<(double X, double Y)> SpreadCloseXs(List<(double X, double Y)> pts, double minX, double maxX, double spacing = 6.0)
        {
            if (pts == null || pts.Count <= 1) return pts;

            var result = pts.Select(p => (X: p.X, Y: p.Y)).ToList();
            int i = 0;
            while (i < result.Count)
            {
                int j = i + 1;
                while (j < result.Count && (result[j].X - result[j - 1].X) <= spacing)
                    j++;

                int groupSize = j - i;
                if (groupSize > 1)
                {
                    double center = 0;
                    for (int k = i; k < j; k++) center += result[k].X;
                    center /= groupSize;

                    double startOffset = -((groupSize - 1) / 2.0) * spacing;
                    var newXs = new double[groupSize];
                    for (int k = 0; k < groupSize; k++)
                        newXs[k] = center + startOffset + k * spacing;

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

        protected override async Task OnInitializedAsync()
        {
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

            _refreshTimer = new System.Threading.Timer(_ => _ = RefreshDataAsync(), null, TimeSpan.FromSeconds(_userUpdateFrequency), TimeSpan.FromSeconds(_userUpdateFrequency));
        }

        private async Task RefreshDataAsync()
        {
            if (_disposed) return;

            try
            {
                await InvokeAsync(async () =>
                {
                    await Task.WhenAll(LoadPersonDataAsync(), LoadChartDataAsync(), LoadRecentAlertsAsync(), LoadRecentMeasurementsAsync());
                    // Reset map so it re-initializes with fresh GPS coordinates
                    _mapInitialized = false;
                    StateHasChanged();
                    await InitTooltipsAsync();
                    await InitMapAsync();
                });
            }
            catch
            {
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

            var first = lines[0].Trim();
            if (first.Contains(','))
            {
                var comps = first.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (comps.Length >= 2 && double.TryParse(comps[0], NumberStyles.Float, CultureInfo.InvariantCulture, out lat) && double.TryParse(comps[1], NumberStyles.Float, CultureInfo.InvariantCulture, out lon))
                    return true;
            }

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
            }
        }

        public async ValueTask DisposeAsync()
        {
            _disposed = true;
            _refreshTimer?.Dispose();
            try
            {
                await JSRuntime.InvokeVoidAsync("chartTooltip.dispose", "hr");
                await JSRuntime.InvokeVoidAsync("chartTooltip.dispose", "temp");
            }
            catch { }
        }

        private string GetVitalStatus(int value, int min, int max)
        {
            if (value < min || value > max)
                return "warning";
            return "normal";
        }

        private string GetVitalStatusText(int value, int min, int max)
        {
            if (value < min)
                return "Sub normal";
            if (value > max)
                return "Peste normal";
            return "Normal";
        }

        private string GetTempStatus(double temp, double min, double max)
        {
            if (temp < min || temp > max)
                return "warning";
            return "normal";
        }

        private string GetTempStatusText(double temp, double min, double max)
        {
            if (temp < min)
                return "Below normal";
            if (temp > max)
                return "Above normal";
            return "Normal";
        }

        private string GetSpO2Status(int spO2)
        {
            if (spO2 < 90)
                return "critical";
            if (spO2 < 95)
                return "warning";
            return "normal";
        }

        private string GetSpO2StatusText(int spO2)
        {
            if (spO2 < 90)
                return "Critical - Low";
            if (spO2 < 95)
                return "Below normal";
            return "Normal";
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

        public class PersonDetail
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = string.Empty;
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
            public int UpdateFrequency { get; set; } = 30;
        }

        public class ChartDataPoint
        {
            public string Day { get; set; } = string.Empty;
            public int Value { get; set; }
            public double ActualValue { get; set; }
            public bool HasData { get; set; } = true;
            public double XFraction { get; set; } = -1;
        }

        public record TooltipPoint(double X, double Y, double Value, string Label, double HitX, double HitWidth);

        private List<TooltipPoint> ComputeTooltipData(
            List<ChartDataPoint> data, double fixedMin, double fixedMax)
        {
            if (data == null || data.Count == 0) return new();

            const double paddingLeft = 90;
            const double paddingRight = 15;
            const double paddingTop = 15;
            var usableWidth = 800 - paddingLeft - paddingRight;
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

        private void GoBack()
        {
            NavigationManager.NavigateTo("/monitored-users");
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
