using Microsoft.AspNetCore.Components;
using LifeAlertPlus.Client.Services;
using LifeAlertPlus.Shared.DTOs.Responses.Measurement;

namespace LifeAlertPlus.Client.Pages.SelectedMonitored
{
    public partial class SelectedMonitored : ComponentBase, IDisposable
    {
        [Parameter]
        public Guid PersonId { get; set; }

        [Inject]
        private NavigationManager NavigationManager { get; set; } = default!;

        [Inject]
        private MonitoredService MonitoredService { get; set; } = default!;

        [Inject]
        private TokenParserService TokenParserService { get; set; } = default!;

        [Inject]
        private MeasurementService MeasurementService { get; set; } = default!;

        private PersonDetail? Person { get; set; }
        private bool IsLoading { get; set; } = true;
        private string? LoadError { get; set; }

        private List<ChartDataPoint> HeartRateHistory { get; set; } = new();
        private List<ChartDataPoint> TemperatureHistory { get; set; } = new();
        private List<(double X, double Y)> HeartRatePoints { get; set; } = new();
        private List<(double X, double Y)> TemperaturePoints { get; set; } = new();
        private List<Alert> RecentAlerts { get; set; } = new();
        private List<Measurement> RecentMeasurements { get; set; } = new();
        private string UserFullName = "";
        private string ProfilePictureUrl = "";
        private ChartViewMode CurrentChartView { get; set; } = ChartViewMode.Weekly;
        private System.Threading.Timer? _refreshTimer;
        private bool _disposed = false;

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
                var monitored = await MonitoredService.GetMonitoredPersonByIdAsync(PersonId);
                if (monitored == null)
                {
                    Person = null;
                    LoadError = "Monitored person not found.";
                    IsLoading = false;
                    return;
                }

                // Get ESP data
                var espData = await MonitoredService.GetEspDataAsync(monitored.DeviceSerialNumber);
                
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

                    // Determine status
                    if (heartRate > 100 || heartRate < 50 || spO2 < 90 || temperature > 37.5 || temperature < 36.0)
                    {
                        status = "Critical";
                    }
                    else if (heartRate > 90 || heartRate < 60 || spO2 < 95 || temperature > 37.0 || temperature < 36.5)
                    {
                        status = "Warning";
                    }
                }
                else
                {
                    status = "Offline";
                }

                // Get last measurement time
                var measurements = await MeasurementService.GetMeasurementsByMonitoredIdAsync(monitored.Id, 1, 1);
                var lastMeasurement = measurements?.FirstOrDefault();
                string lastUpdate = lastMeasurement != null 
                    ? lastMeasurement.CreatedAt.ToLocalTime().ToString("MMMM dd, yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture)
                    : "No data";

                Person = new PersonDetail
                {
                    Id = monitored.Id,
                    Name = $"{monitored.FirstName} {monitored.LastName}".Trim(),
                    Age = GetAge(monitored.Birthdate),
                    Relationship = monitored.Gender ?? "N/A",
                    HeartRate = heartRate,
                    SpO2 = spO2,
                    Temperature = temperature,
                    GPS = gps,
                    Status = status,
                    LastUpdate = lastUpdate,
                    Location = string.IsNullOrWhiteSpace(monitored.Address) ? "N/A" : monitored.Address,
                    DeviceSerial = monitored.DeviceSerialNumber
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

        private List<ChartDataPoint> HeartRateHistoryFiltered =>
            HeartRateHistory.Where(d => d.HasData).ToList();
        private List<ChartDataPoint> TemperatureHistoryFiltered =>
            TemperatureHistory.Where(d => d.HasData).ToList();

        private async Task LoadChartDataAsync()
        {
            try
            {
                // Get measurements from last 7 days
                var measurements = await MeasurementService.GetMeasurementsByMonitoredIdAsync(PersonId, 1, 1000);
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
            }
            catch
            {
                LoadEmptyChartData();
            }
        }

        private void LoadDailyChartData(List<MeasurementResponseDTO> measurements)
        {
            var today = DateTime.Now.Date;

            var todayMs = measurements
                .Where(m => m.CreatedAt.ToLocalTime().Date == today)
                .OrderBy(m => m.CreatedAt)
                .ToList();

            if (!todayMs.Any()) { LoadEmptyChartData(); return; }

            // Grupare pe interval de 5 minute
            var grouped = todayMs
                .GroupBy(m => new {
                    Hour = m.CreatedAt.ToLocalTime().Hour,
                    Bucket = m.CreatedAt.ToLocalTime().Minute / 5
                })
                .Select(g => new {
                    Time = new TimeSpan(g.Key.Hour, g.Key.Bucket * 5, 0),
                    Pulse = g.Average(x => x.Pulse),
                    Temp = g.Average(x => x.Temperature)
                })
                .OrderBy(x => x.Time)
                .ToList();

            HeartRateHistory = grouped.Select(g => new ChartDataPoint
            {
                Day = g.Time.ToString(@"hh\:mm"),
                ActualValue = g.Pulse,
                HasData = true,
                XFraction = g.Time.TotalHours / 24.0
            }).ToList();

            TemperatureHistory = grouped.Select(g => new ChartDataPoint
            {
                Day = g.Time.ToString(@"hh\:mm"),
                ActualValue = g.Temp,
                HasData = true,
                XFraction = g.Time.TotalHours / 24.0
            }).ToList();

            // smoothing mic (nu exagerat)
            var hrSmooth = SmoothValues(HeartRateHistory.Select(x => x.ActualValue).ToList(), 3);
            var tSmooth  = SmoothValues(TemperatureHistory.Select(x => x.ActualValue).ToList(), 3);

            for (int i = 0; i < HeartRateHistory.Count; i++)
                HeartRateHistory[i].ActualValue = hrSmooth[i];

            for (int i = 0; i < TemperatureHistory.Count; i++)
                TemperatureHistory[i].ActualValue = tSmooth[i];
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

        private bool ShowDataPoints => false; // points hidden — line only

        private void LoadWeeklyChartData(List<MeasurementResponseDTO> measurements)
        {
            var today = DateTime.Now.Date;
            // 7 fixed day slots: 6 days ago → today
            var days = Enumerable.Range(0, 7).Select(i => today.AddDays(i - 6)).ToList();

            var hrByDay = measurements
                .Where(m => m.CreatedAt.ToLocalTime().Date >= days[0])
                .GroupBy(m => m.CreatedAt.ToLocalTime().Date)
                .ToDictionary(g => g.Key, g => g.Average(m => (double)m.Pulse));

            var tempByDay = measurements
                .Where(m => m.CreatedAt.ToLocalTime().Date >= days[0])
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
            StateHasChanged();
            await Task.Delay(50);
            await LoadChartDataAsync();
        }

        private List<(double X, double Y)> ComputePoints(List<ChartDataPoint> data)
        {
            return ComputePointsWithRange(data, 0, 0);
        }

        private List<(double X, double Y)> ComputePointsWithRange(
            List<ChartDataPoint> data, double fixedMin, double fixedMax)
        {
            if (data == null || data.Count == 0) return new();

            const double paddingLeft = 70;
            const double paddingRight = 15;
            const double paddingTop = 15;
            var usableWidth = 800 - paddingLeft - paddingRight;  // 715
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
            return data
                .Select((d, i) => (
                    HasData: d.HasData,
                    X: paddingLeft + (d.XFraction >= 0
                        ? d.XFraction * usableWidth
                        : (n <= 1 ? usableWidth / 2.0 : (double)i / (n - 1) * usableWidth)),
                    Y: paddingTop + usableHeight * (1.0 - Math.Clamp((d.ActualValue - minVal) / range, 0.0, 1.0))
                ))
                .Where(p => p.HasData)
                .Select(p => (X: p.X, Y: p.Y))
                .ToList();
        }

        private List<(string Label, double X)> GetXAxisLabels()
        {
            const double paddingLeft = 70;
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
        private string GenerateSmoothPath(List<(double X, double Y)> pts)
        {
            if (pts == null || pts.Count < 2) return "";

            var path = new System.Text.StringBuilder();
            path.Append($"M {pts[0].X:F2} {pts[0].Y:F2}");

            for (int i = 0; i < pts.Count - 1; i++)
            {
                var p0 = i > 0 ? pts[i - 1] : pts[i];
                var p1 = pts[i];
                var p2 = pts[i + 1];
                var p3 = i < pts.Count - 2 ? pts[i + 2] : p2;

                double cp1x = p1.X + (p2.X - p0.X) / 6;
                double cp1y = p1.Y + (p2.Y - p0.Y) / 6;

                double cp2x = p2.X - (p3.X - p1.X) / 6;
                double cp2y = p2.Y - (p3.Y - p1.Y) / 6;

                path.Append($" C {cp1x:F2} {cp1y:F2}, {cp2x:F2} {cp2y:F2}, {p2.X:F2} {p2.Y:F2}");
            }

            return path.ToString();
}

        private void LoadEmptyChartData()
        {
            HeartRateHistory = new List<ChartDataPoint>();
            TemperatureHistory = new List<ChartDataPoint>();
            HeartRatePoints = new List<(double X, double Y)>();
            TemperaturePoints = new List<(double X, double Y)>();
        }

        private async Task LoadRecentAlertsAsync()
        {
            try
            {
                var measurements = await MeasurementService.GetMeasurementsByMonitoredIdAsync(PersonId, 1, 50);
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
                var measurements = await MeasurementService.GetMeasurementsByMonitoredIdAsync(PersonId, 1, 10);
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
            }
            else
            {
                UserFullName = "User";
            }

            await LoadPersonDataAsync();
            await LoadChartDataAsync();
            await LoadRecentAlertsAsync();
            await LoadRecentMeasurementsAsync();

            // Start auto-refresh timer (30 seconds)
            _refreshTimer = new System.Threading.Timer(async _ => await RefreshDataAsync(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        private async Task RefreshDataAsync()
        {
            if (_disposed) return;

            try
            {
                await InvokeAsync(async () =>
                {
                    await LoadPersonDataAsync();

                    HeartRateHistory = new List<ChartDataPoint>();
                    TemperatureHistory = new List<ChartDataPoint>();
                    HeartRatePoints = new List<(double X, double Y)>();
                    TemperaturePoints = new List<(double X, double Y)>();
                    StateHasChanged();
                    await Task.Delay(50);

                    await LoadChartDataAsync();
                    await LoadRecentAlertsAsync();
                    await LoadRecentMeasurementsAsync();
                    StateHasChanged();
                });
            }
            catch
            {
                // Ignore errors during auto-refresh
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _refreshTimer?.Dispose();
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

        private string GetTempStatus(double temp)
        {
            if (temp < 36.0 || temp > 37.5)
                return "warning";
            return "normal";
        }

        private string GetTempStatusText(double temp)
        {
            if (temp < 36.0)
                return "Below normal";
            if (temp > 37.5)
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
                return "warning";
            return "normal";
        }

        private string GetGPSStatusText(string gps)
        {
            if (gps == "No data" || string.IsNullOrEmpty(gps))
                return "No GPS signal";
            return "Signal available";
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
            NavigationManager.NavigateTo("/monitored");
        }

        public class PersonDetail
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public int Age { get; set; }
            public string Relationship { get; set; } = string.Empty;
            public int HeartRate { get; set; }
            public int SpO2 { get; set; }
            public double Temperature { get; set; }
            public string GPS { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public string LastUpdate { get; set; } = string.Empty;
            public string Location { get; set; } = string.Empty;
            public string DeviceSerial { get; set; } = string.Empty;
        }

        public class ChartDataPoint
        {
            public string Day { get; set; } = string.Empty;
            public int Value { get; set; }
            public double ActualValue { get; set; }
            public bool HasData { get; set; } = true;
            public double XFraction { get; set; } = -1; // -1 = index-based; 0.0-1.0 = explicit
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
