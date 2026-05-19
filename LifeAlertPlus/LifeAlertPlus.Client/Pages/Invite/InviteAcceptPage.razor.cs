using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Shared.DTOs.Responses.Email;
using LifeAlertPlus.Shared.DTOs.Responses.Measurement;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace LifeAlertPlus.Client.Pages.Invite
{
    public partial class InviteAcceptPage : ComponentBase
    {
        [Inject] private HttpClient Http { get; set; } = default!;
        [Inject] private NavigationManager Navigation { get; set; } = default!;
        [Inject] private Services.LanguageService Lang { get; set; } = default!;
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

        private string T(string key) => Lang.T(key);

        private bool IsLoading { get; set; } = true;
        private string? ErrorMessage { get; set; }

        private InvitationInfoResponseDTO? InvitationInfo { get; set; }
        private LifeAlertPlus.Domain.Entities.Monitored? Patient { get; set; }

        private List<MeasurementResponseDTO> Measurements { get; set; } = new();
        private int CurrentPage { get; set; } = 1;
        private const int PageSize = 12;
        private DayOfWeek _firstDayOfWeek = DayOfWeek.Monday;

        private string? _token;

        private ChartViewMode CurrentChartView { get; set; } = ChartViewMode.Daily;
        private int _weekOffset;
        private int _dayOffset;
        private string _chartWeekLabel = string.Empty;
        private string _chartDayLabel = string.Empty;
        private bool _hasPrevWeekData;
        private bool _hasPrevDayData;
        private List<ChartDataPoint> HeartRateHistory { get; set; } = new();
        private List<ChartDataPoint> TemperatureHistory { get; set; } = new();
        private List<(double X, double Y)> HeartRatePoints { get; set; } = new();
        private List<(double X, double Y)> TemperaturePoints { get; set; } = new();

        private bool _showExportModal;
        private bool _isExporting;
        private DateTime? _exportStartDate;
        private DateTime? _exportEndDate;
        private DateTime? _exportMinDate;
        private DateTime? _exportMaxDate;
        private int? _exportMeasurementCount;
        private int _exportDistinctDays;

        private enum ChartViewMode
        {
            Daily,
            Weekly
        }

        // Computed / UI helpers
        private MeasurementResponseDTO? LatestMeasurement => Measurements
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefault();

        private MeasurementResponseDTO? CurrentMeasurement => LatestMeasurement?.CreatedAt.ToLocalTime().Date == DateTime.Now.Date
            ? LatestMeasurement
            : null;

        private List<MeasurementResponseDTO> RecentMeasurements => Measurements
            .OrderByDescending(m => m.CreatedAt)
            .Take(50)
            .ToList();

        private List<MeasurementResponseDTO> PagedMeasurements => RecentMeasurements
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize)
            .ToList();

        private int TotalPages => Math.Max(1, (int)Math.Ceiling(RecentMeasurements.Count / (double)PageSize));
        private bool HasPrevious => CurrentPage > 1;
        private bool HasNext => CurrentPage < TotalPages;

        private int TotalMeasurements => Measurements.Count;
        private int CriticalCount => Measurements.Count(m => GetStatus(m) == "critical");
        private int AlertCount => Measurements.Count(m => GetStatus(m) == "alert");
        private int FallCount => Measurements.Count(m => m.IsFall);
        private int DaysWithData => Measurements
            .Select(m => m.CreatedAt.ToLocalTime().Date)
            .Distinct()
            .Count();

        private double AveragePulse => Measurements.Any() ? Measurements.Average(m => m.Pulse) : 0;
        private double AverageTemperature => Measurements.Any() ? Measurements.Average(m => m.Temperature) : 0;
        private double AverageSpO2 => Measurements.Where(m => m.SpO2 > 0).DefaultIfEmpty().Average(m => m?.SpO2 ?? 0);

        private List<MeasurementResponseDTO> ChartPoints => Measurements
            .OrderBy(m => m.CreatedAt)
            .TakeLast(200)
            .ToList();

        private string Ui(string en, string ro) => Lang.CurrentLanguage == "ro" ? ro : en;

        protected override async Task OnInitializedAsync()
        {
            IsLoading = true;
            ErrorMessage = null;

            try
            {
                _token = GetQueryParam("token");
                if (string.IsNullOrWhiteSpace(_token))
                {
                    ErrorMessage = T("invite.missingToken");
                    return;
                }

                // 1) Invitation info (validates token + expiration)
                var infoResp = await Http.GetAsync($"api/invitations/info?token={Uri.EscapeDataString(_token)}");
                if (!infoResp.IsSuccessStatusCode)
                {
                    ErrorMessage = infoResp.StatusCode == HttpStatusCode.NotFound
                        ? T("invite.invalid")
                        : T("invite.loadError");
                    return;
                }

                InvitationInfo = await infoResp.Content.ReadFromJsonAsync<InvitationInfoResponseDTO>();
                if (InvitationInfo == null)
                {
                    ErrorMessage = T("invite.invalid");
                    return;
                }

                if (InvitationInfo.IsExpired)
                {
                    ErrorMessage = T("invite.expired");
                    return;
                }

                // 2+3) Patient data and measurements in parallel
                var patientTask = Http.GetAsync($"api/invitations/patient?token={Uri.EscapeDataString(_token)}");
                var measTask    = Http.GetAsync($"api/invitations/measurements?token={Uri.EscapeDataString(_token)}&pageNumber=1&pageSize=1000");
                await Task.WhenAll(patientTask, measTask);

                var patientResp = await patientTask;
                if (!patientResp.IsSuccessStatusCode)
                {
                    ErrorMessage = T("invite.invalid");
                    return;
                }

                Patient = await patientResp.Content.ReadFromJsonAsync<LifeAlertPlus.Domain.Entities.Monitored>();

                var measResp = await measTask;
                if (measResp.IsSuccessStatusCode)
                {
                    var list = await measResp.Content.ReadFromJsonAsync<List<MeasurementResponseDTO>>();
                    Measurements = list ?? new List<MeasurementResponseDTO>();
                    CurrentPage = 1;
                    LoadChartData();
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"{T("invite.loadError")}: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void LoadChartData()
        {
            if (!Measurements.Any())
            {
                LoadEmptyChartData();
                return;
            }

            if (CurrentChartView == ChartViewMode.Daily)
                LoadDailyChartData(Measurements);
            else
                LoadWeeklyChartData(Measurements);

            HeartRatePoints = ComputePointsWithRange(HeartRateHistory, 40, 120);
            TemperaturePoints = ComputePointsWithRange(TemperatureHistory, 35, 39);
        }

        private void LoadDailyChartData(List<MeasurementResponseDTO> measurements)
        {
            var targetDay = DateTime.Now.Date.AddDays(_dayOffset);
            _chartDayLabel = targetDay.ToString("dddd, dd MMM yyyy");

            var prevDay = targetDay.AddDays(-1);
            _hasPrevDayData = measurements.Any(m => m.CreatedAt.ToLocalTime().Date == prevDay);

            var dayMeasurements = measurements
                .Where(m => m.CreatedAt.ToLocalTime().Date == targetDay)
                .OrderBy(m => m.CreatedAt)
                .ToList();

            if (!dayMeasurements.Any())
            {
                LoadEmptyChartData();
                return;
            }

            HeartRateHistory = dayMeasurements.Select(m => new ChartDataPoint
            {
                Day = m.CreatedAt.ToLocalTime().ToString("HH:mm"),
                ActualValue = m.Pulse,
                HasData = m.Pulse > 0,
                XFraction = m.CreatedAt.ToLocalTime().TimeOfDay.TotalHours / 24.0
            }).ToList();

            TemperatureHistory = dayMeasurements.Select(m => new ChartDataPoint
            {
                Day = m.CreatedAt.ToLocalTime().ToString("HH:mm"),
                ActualValue = m.Temperature,
                HasData = m.Temperature > 0,
                XFraction = m.CreatedAt.ToLocalTime().TimeOfDay.TotalHours / 24.0
            }).ToList();
        }

        private void LoadWeeklyChartData(List<MeasurementResponseDTO> measurements)
        {
            var today = DateTime.Now.Date;
            var diff = ((int)today.DayOfWeek - (int)_firstDayOfWeek + 7) % 7;
            var currentWeekStart = today.AddDays(-diff);
            var weekStart = currentWeekStart.AddDays(_weekOffset * 7);
            var days = Enumerable.Range(0, 7).Select(i => weekStart.AddDays(i)).ToList();

            _chartWeekLabel = $"{weekStart:dd MMM} - {days[6]:dd MMM yyyy}";

            var prevWeekStart = weekStart.AddDays(-7);
            var prevWeekEnd = weekStart.AddDays(-1);
            _hasPrevWeekData = measurements.Any(m =>
            {
                var d = m.CreatedAt.ToLocalTime().Date;
                return d >= prevWeekStart && d <= prevWeekEnd;
            });

            var hrByDay = measurements
                .Where(m => m.Pulse > 0 && m.CreatedAt.ToLocalTime().Date >= days[0] && m.CreatedAt.ToLocalTime().Date <= days[6])
                .GroupBy(m => m.CreatedAt.ToLocalTime().Date)
                .ToDictionary(g => g.Key, g => g.Average(m => (double)m.Pulse));

            var tempByDay = measurements
                .Where(m => m.Temperature > 0 && m.CreatedAt.ToLocalTime().Date >= days[0] && m.CreatedAt.ToLocalTime().Date <= days[6])
                .GroupBy(m => m.CreatedAt.ToLocalTime().Date)
                .ToDictionary(g => g.Key, g => g.Average(m => m.Temperature));

            HeartRateHistory = days.Select(day => new ChartDataPoint
            {
                Day = day.ToString("ddd", CultureInfo.InvariantCulture),
                ActualValue = hrByDay.TryGetValue(day, out var v) ? v : 0,
                HasData = hrByDay.ContainsKey(day)
            }).ToList();

            TemperatureHistory = days.Select(day => new ChartDataPoint
            {
                Day = day.ToString("ddd", CultureInfo.InvariantCulture),
                ActualValue = tempByDay.TryGetValue(day, out var v) ? v : 0,
                HasData = tempByDay.ContainsKey(day)
            }).ToList();
        }

        private Task SwitchChartView(ChartViewMode mode)
        {
            CurrentChartView = mode;
            _weekOffset = 0;
            _dayOffset = 0;
            LoadChartData();
            return Task.CompletedTask;
        }

        private Task GoToPreviousWeek()
        {
            if (!_hasPrevWeekData) return Task.CompletedTask;
            _weekOffset--;
            LoadChartData();
            return Task.CompletedTask;
        }

        private Task GoToNextWeek()
        {
            if (_weekOffset >= 0) return Task.CompletedTask;
            _weekOffset++;
            LoadChartData();
            return Task.CompletedTask;
        }

        private Task GoToPreviousDay()
        {
            if (!_hasPrevDayData) return Task.CompletedTask;
            _dayOffset--;
            LoadChartData();
            return Task.CompletedTask;
        }

        private Task GoToNextDay()
        {
            if (_dayOffset >= 0) return Task.CompletedTask;
            _dayOffset++;
            LoadChartData();
            return Task.CompletedTask;
        }

        private void LoadEmptyChartData()
        {
            HeartRateHistory = new();
            TemperatureHistory = new();
            HeartRatePoints = new();
            TemperaturePoints = new();
        }

        private List<(double X, double Y)> ComputePointsWithRange(List<ChartDataPoint> data, double fixedMin, double fixedMax)
        {
            if (data == null || data.Count == 0) return new();

            const double paddingLeft = 90;
            const double paddingRight = 15;
            const double paddingTop = 15;
            var usableWidth = 800 - paddingLeft - paddingRight;
            var usableHeight = 145.0;

            var range = Math.Max(0.001, fixedMax - fixedMin);
            var count = data.Count;
            var points = data
                .Select((d, i) => (
                    d.HasData,
                    X: paddingLeft + (d.XFraction >= 0
                        ? d.XFraction * usableWidth
                        : count <= 1 ? usableWidth / 2.0 : (double)i / (count - 1) * usableWidth),
                    Y: paddingTop + usableHeight * (1.0 - Math.Clamp((d.ActualValue - fixedMin) / range, 0.0, 1.0))))
                .Where(p => p.HasData)
                .OrderBy(p => p.X)
                .Select(p => (p.X, p.Y))
                .ToList();

            return SpreadCloseXs(points, paddingLeft, paddingLeft + usableWidth, CurrentChartView == ChartViewMode.Daily ? 12.0 : 8.0);
        }

        private List<(string Label, double X)> GetXAxisLabels()
        {
            const double paddingLeft = 90;
            const double paddingRight = 15;
            var usableWidth = 800 - paddingLeft - paddingRight;

            if (CurrentChartView == ChartViewMode.Daily)
            {
                return new[] { 0, 4, 8, 12, 16, 20, 23 }
                    .Select(h => (Label: $"{h:00}:00", X: paddingLeft + (h / 23.0) * usableWidth))
                    .ToList();
            }

            var data = HeartRateHistory;
            if (data.Count == 0) return new();
            return data
                .Select((d, i) => (
                    Label: d.Day,
                    X: paddingLeft + (data.Count <= 1 ? usableWidth / 2.0 : (double)i / (data.Count - 1) * usableWidth)))
                .ToList();
        }

        private static string F(double value) => value.ToString("F2", CultureInfo.InvariantCulture);

        private string GenerateAreaPath(List<(double X, double Y)> points, double baseline = 160)
        {
            if (points == null || points.Count == 0) return string.Empty;
            if (points.Count == 1)
                return $"M {F(points[0].X)} {F(points[0].Y)} L {F(points[0].X + 1.0)} {F(points[0].Y)} L {F(points[0].X + 1.0)} {F(baseline)} L {F(points[0].X)} {F(baseline)} Z";

            var linePath = GenerateSmoothPath(points);
            return $"{linePath} L {F(points[^1].X)} {F(baseline)} L {F(points[0].X)} {F(baseline)} Z";
        }

        private string GenerateSmoothPath(List<(double X, double Y)> points)
        {
            if (points == null || points.Count == 0) return string.Empty;
            if (points.Count == 1) return $"M {F(points[0].X)} {F(points[0].Y)} L {F(points[0].X + 1.0)} {F(points[0].Y)}";
            if (points.Count == 2) return $"M {F(points[0].X)} {F(points[0].Y)} L {F(points[1].X)} {F(points[1].Y)}";

            var n = points.Count;
            var dx = new double[n - 1];
            var dy = new double[n - 1];
            var slopes = new double[n - 1];
            for (var i = 0; i < n - 1; i++)
            {
                dx[i] = points[i + 1].X - points[i].X;
                dy[i] = points[i + 1].Y - points[i].Y;
                slopes[i] = dx[i] < 1e-10 ? 0 : dy[i] / dx[i];
            }

            var tangents = new double[n];
            tangents[0] = slopes[0];
            tangents[n - 1] = slopes[n - 2];
            for (var i = 1; i < n - 1; i++)
                tangents[i] = slopes[i - 1] * slopes[i] <= 0 ? 0 : (slopes[i - 1] + slopes[i]) / 2.0;

            for (var i = 0; i < n - 1; i++)
            {
                if (Math.Abs(slopes[i]) < 1e-10)
                {
                    tangents[i] = 0;
                    tangents[i + 1] = 0;
                    continue;
                }

                var alpha = tangents[i] / slopes[i];
                var beta = tangents[i + 1] / slopes[i];
                var magnitude = alpha * alpha + beta * beta;
                if (magnitude <= 9) continue;

                var tau = 3.0 / Math.Sqrt(magnitude);
                tangents[i] = tau * alpha * slopes[i];
                tangents[i + 1] = tau * beta * slopes[i];
            }

            var path = new StringBuilder();
            path.Append($"M {F(points[0].X)} {F(points[0].Y)}");
            for (var i = 0; i < n - 1; i++)
            {
                var segment = dx[i] / 3.0;
                var cp1X = points[i].X + segment;
                var cp1Y = points[i].Y + tangents[i] * segment;
                var cp2X = points[i + 1].X - segment;
                var cp2Y = points[i + 1].Y - tangents[i + 1] * segment;
                path.Append($" C {F(cp1X)} {F(cp1Y)}, {F(cp2X)} {F(cp2Y)}, {F(points[i + 1].X)} {F(points[i + 1].Y)}");
            }

            return path.ToString();
        }

        private static List<(double X, double Y)> SpreadCloseXs(List<(double X, double Y)> points, double minX, double maxX, double spacing)
        {
            if (points.Count <= 1) return points;

            var result = points.Select(p => (p.X, p.Y)).ToList();
            var i = 0;
            while (i < result.Count)
            {
                var j = i + 1;
                while (j < result.Count && result[j].X - result[j - 1].X <= spacing)
                    j++;

                var groupSize = j - i;
                if (groupSize > 1)
                {
                    var center = result.Skip(i).Take(groupSize).Average(p => p.X);
                    var startOffset = -((groupSize - 1) / 2.0) * spacing;
                    var newXs = Enumerable.Range(0, groupSize).Select(k => center + startOffset + k * spacing).ToArray();
                    if (newXs[0] < minX)
                    {
                        var shift = minX - newXs[0];
                        for (var k = 0; k < groupSize; k++) newXs[k] += shift;
                    }
                    if (newXs[^1] > maxX)
                    {
                        var shift = maxX - newXs[^1];
                        for (var k = 0; k < groupSize; k++) newXs[k] += shift;
                    }
                    for (var k = 0; k < groupSize; k++)
                        result[i + k] = (newXs[k], result[i + k].Y);
                }
                i = j;
            }

            return result;
        }

        private void GoToPage(int page)
        {
            if (page < 1 || page > TotalPages || page == CurrentPage)
                return;

            CurrentPage = page;
        }

        private List<int> GetPageNumbers()
        {
            var pages = new List<int>();
            if (TotalPages <= 7)
            {
                for (var i = 1; i <= TotalPages; i++)
                    pages.Add(i);

                return pages;
            }

            pages.Add(1);
            pages.Add(2);

            if (CurrentPage > 4)
                pages.Add(-1);

            var start = Math.Max(3, CurrentPage - 1);
            var end = Math.Min(TotalPages - 2, CurrentPage + 1);
            for (var i = start; i <= end; i++)
                pages.Add(i);

            if (CurrentPage < TotalPages - 3)
                pages.Add(-1);

            pages.Add(TotalPages - 1);
            pages.Add(TotalPages);

            return pages.Distinct().ToList();
        }

        private string GetPatientFullName()
        {
            if (Patient == null)
                return string.Empty;

            return $"{Patient.FirstName} {Patient.LastName}".Trim();
        }

        private int? GetAge()
        {
            if (Patient?.Birthdate == null)
                return null;

            var today = DateTime.Today;
            var age = today.Year - Patient.Birthdate.Value.Year;
            if (Patient.Birthdate.Value.Date > today.AddYears(-age))
                age--;

            return Math.Max(age, 0);
        }

        private string GetStatus(MeasurementResponseDTO m)
        {
            var minHr = Patient?.MinHeartRate ?? 60;
            var maxHr = Patient?.MaxHeartRate ?? 100;
            var minTemp = Patient?.MinTemperature ?? 36.0;
            var maxTemp = Patient?.MaxTemperature ?? 37.5;

            if (m.IsFall) return "critical";
            if (m.Pulse > maxHr + 30 || m.Pulse < minHr - 20) return "critical";
            if (m.Temperature > maxTemp + 1.0 || m.Temperature < minTemp - 1.0) return "critical";
            if (m.SpO2 > 0 && m.SpO2 < 90) return "critical";

            if (m.Pulse > maxHr || m.Pulse < minHr) return "alert";
            if (m.Temperature > maxTemp || m.Temperature < minTemp) return "alert";
            if (m.SpO2 > 0 && m.SpO2 < 95) return "alert";

            return "normal";
        }

        private string GetStatusLabel(string status) => status switch
        {
            "critical" => Ui("Critical", "Critic"),
            "alert" => Ui("Attention", "Atentie"),
            "normal" => Ui("Stable", "Stabil"),
            _ => "-"
        };

        private string GetVitalStatusText(MeasurementResponseDTO measurement, string type)
        {
            return type switch
            {
                "hr" when measurement.Pulse < (Patient?.MinHeartRate ?? 60) => Ui("Below normal", "Sub normal"),
                "hr" when measurement.Pulse > (Patient?.MaxHeartRate ?? 100) => Ui("Above normal", "Peste normal"),
                "temp" when measurement.Temperature < (Patient?.MinTemperature ?? 36.0) => Ui("Below normal", "Sub normal"),
                "temp" when measurement.Temperature > (Patient?.MaxTemperature ?? 37.5) => Ui("Above normal", "Peste normal"),
                "spo2" when measurement.SpO2 < 90 => Ui("Critical - low", "Critic - scazut"),
                "spo2" when measurement.SpO2 < 95 => Ui("Below normal", "Sub normal"),
                _ => Ui("Normal", "Normal")
            };
        }

        private string GetCurrentStatusText(MeasurementResponseDTO measurement)
        {
            if (measurement.IsFall)
                return Ui("A fall was detected in the current measurement.", "A fost detectata o cadere in masuratoarea curenta.");

            return GetStatus(measurement) switch
            {
                "critical" => Ui("Current vital signs include critical values.", "Semnele vitale curente includ valori critice."),
                "alert" => Ui("Current vital signs need attention.", "Semnele vitale curente necesita atentie."),
                _ => Ui("Current vital signs are inside the expected ranges.", "Semnele vitale curente sunt in intervalele asteptate.")
            };
        }

        private string GetStatusIcon(string status) => status switch
        {
            "critical" => "!",
            "alert" => "?",
            "normal" => "OK",
            _ => "-"
        };

        private string GetCellClass(MeasurementResponseDTO m, string type)
        {
            var minHr = Patient?.MinHeartRate ?? 60;
            var maxHr = Patient?.MaxHeartRate ?? 100;
            var minTemp = Patient?.MinTemperature ?? 36.0;
            var maxTemp = Patient?.MaxTemperature ?? 37.5;

            return type switch
            {
                "hr" => m.Pulse > maxHr + 30 || m.Pulse < minHr - 20 ? "cell-critical"
                    : m.Pulse > maxHr || m.Pulse < minHr ? "cell-alert" : "cell-normal",
                "temp" => m.Temperature > maxTemp + 1.0 || m.Temperature < minTemp - 1.0 ? "cell-critical"
                    : m.Temperature > maxTemp || m.Temperature < minTemp ? "cell-alert" : "cell-normal",
                "spo2" => m.SpO2 > 0 && m.SpO2 < 90 ? "cell-critical"
                    : m.SpO2 > 0 && m.SpO2 < 95 ? "cell-alert" : "cell-normal",
                _ => string.Empty
            };
        }

        private List<KeyMoment> KeyMoments => Measurements
            .OrderByDescending(m => GetMomentRank(m))
            .ThenByDescending(m => m.CreatedAt)
            .Where(m => GetMomentRank(m) > 0)
            .Take(6)
            .Select(CreateKeyMoment)
            .ToList();

        private int GetMomentRank(MeasurementResponseDTO m)
        {
            if (m.IsFall) return 5;
            if (GetStatus(m) == "critical") return 4;
            if (m.SpO2 > 0 && m.SpO2 < 95) return 3;
            if (GetStatus(m) == "alert") return 2;
            return 0;
        }

        private KeyMoment CreateKeyMoment(MeasurementResponseDTO m)
        {
            if (m.IsFall)
            {
                return new KeyMoment(
                    Ui("Fall detected", "Cadere detectata"),
                    Ui("The device marked a fall event. Review the context and vital signs around this moment.", "Dispozitivul a marcat un eveniment de cadere. Verifica semnele vitale din jurul acestui moment."),
                    m.CreatedAt,
                    "critical");
            }

            if (GetStatus(m) == "critical")
            {
                return new KeyMoment(
                    Ui("Critical vital sign", "Semn vital critic"),
                    $"HR {m.Pulse:F0} bpm, Temp {m.Temperature:F1} C, SpO2 {m.SpO2:F0}%",
                    m.CreatedAt,
                    "critical");
            }

            return new KeyMoment(
                Ui("Out-of-range reading", "Valoare in afara intervalului"),
                $"HR {m.Pulse:F0} bpm, Temp {m.Temperature:F1} C, SpO2 {m.SpO2:F0}%",
                m.CreatedAt,
                "alert");
        }

        private string GetSummaryText()
        {
            if (!Measurements.Any())
                return Ui("No measurements are available for this invitation yet.", "Nu exista inca masuratori disponibile pentru aceasta invitatie.");

            if (CriticalCount > 0)
                return Ui("Critical moments were detected in the shared monitoring history.", "Au fost detectate momente critice in istoricul de monitorizare partajat.");

            if (AlertCount > 0)
                return Ui("Some readings are outside the expected range and deserve attention.", "Unele valori sunt in afara intervalului asteptat si merita atentie.");

            return Ui("The available readings are currently stable.", "Valorile disponibile sunt in prezent stabile.");
        }

        private void ExportPdfAsync()
        {
            if (!Measurements.Any())
            {
                _exportStartDate = DateTime.Today;
                _exportEndDate = DateTime.Today;
                _exportMinDate = DateTime.Today;
                _exportMaxDate = DateTime.Today;
                _exportMeasurementCount = 0;
                _exportDistinctDays = 0;
                _showExportModal = true;
                return;
            }

            var dates = Measurements.Select(m => m.CreatedAt.ToLocalTime().Date).ToList();
            _exportMinDate = dates.Min();
            _exportMaxDate = dates.Max();
            _exportEndDate = _exportMaxDate;
            _exportStartDate = _exportEndDate.Value.AddDays(-6);
            if (_exportStartDate < _exportMinDate)
                _exportStartDate = _exportMinDate;

            UpdateExportStats();
            _showExportModal = true;
        }

        private void CloseExportModal()
        {
            _showExportModal = false;
        }

        private void UpdateExportStats()
        {
            if (!_exportStartDate.HasValue || !_exportEndDate.HasValue)
            {
                _exportMeasurementCount = null;
                _exportDistinctDays = 0;
                return;
            }

            var start = _exportStartDate.Value.Date;
            var end = _exportEndDate.Value.Date;
            var filtered = Measurements
                .Where(m =>
                {
                    var date = m.CreatedAt.ToLocalTime().Date;
                    return date >= start && date <= end;
                })
                .ToList();

            _exportMeasurementCount = filtered.Count;
            _exportDistinctDays = filtered.Select(m => m.CreatedAt.ToLocalTime().Date).Distinct().Count();
        }

        private async Task GenerateExportPdfAsync()
        {
            if (Patient == null || !_exportStartDate.HasValue || !_exportEndDate.HasValue)
                return;

            UpdateExportStats();
            if (_exportDistinctDays < 7)
                return;

            _isExporting = true;
            try
            {
                var start = _exportStartDate.Value.Date;
                var end = _exportEndDate.Value.Date;
                var filtered = Measurements
                    .Where(m =>
                    {
                        var date = m.CreatedAt.ToLocalTime().Date;
                        return date >= start && date <= end;
                    })
                    .OrderBy(m => m.CreatedAt)
                    .ToList();

                var periodLabel = $"{start:dd MMM yyyy} - {end:dd MMM yyyy}";
                var patientName = GetPatientFullName();
                var summary = BuildExportSummary(filtered);

                var pdfData = new
                {
                    reportTitle = T("export.reportTitle"),
                    generatedAt = $"{T("export.generatedAt")} {DateTime.Now:dd MMM yyyy, HH:mm}",
                    patientSectionTitle = T("export.patientInfo"),
                    firstNameLabel = T("selected.firstName"),
                    patientFirstName = Patient.FirstName,
                    lastNameLabel = T("selected.lastName"),
                    patientLastName = Patient.LastName,
                    ageLabel = T("export.age"),
                    patientAge = GetAge().HasValue ? $"{GetAge()} {T("selected.years")}" : "-",
                    addressLabel = T("export.address"),
                    address = string.IsNullOrWhiteSpace(Patient.Address) ? "-" : Patient.Address,
                    periodSectionTitle = T("export.selectedPeriod"),
                    period = periodLabel,
                    summarySectionTitle = T("export.summary"),
                    summary,
                    hMetric = T("export.metric"),
                    hAvg = T("export.avg"),
                    hMin = T("export.min"),
                    hMax = T("export.max"),
                    hStdDev = T("export.stdDev"),
                    weeklySectionTitle = T("export.weeklyBreakdown"),
                    weeklyBreakdown = BuildWeeklyBreakdown(filtered),
                    dailySectionTitle = T("export.dailyBreakdown"),
                    dailyBreakdown = BuildDailyBreakdown(filtered),
                    alertsSectionTitle = T("export.alerts"),
                    alerts = filtered.Where(m => GetStatus(m) == "alert").Select(ToExportEvent).ToArray(),
                    criticalsSectionTitle = T("export.criticalEvents"),
                    criticals = filtered.Where(m => GetStatus(m) == "critical").Select(ToExportEvent).ToArray(),
                    rawDataSectionTitle = T("export.rawData"),
                    rawData = filtered.Select(m => new
                    {
                        date = m.CreatedAt.ToLocalTime().ToString("dd MMM yyyy, HH:mm"),
                        pulse = $"{m.Pulse:F0}",
                        temp = $"{m.Temperature:F1}",
                        spo2 = $"{m.SpO2:F0}",
                        activity = string.IsNullOrWhiteSpace(m.Activity) ? "-" : m.Activity,
                        fall = m.IsFall ? Ui("Yes", "Da") : "-"
                    }).ToArray(),
                    interpretationSectionTitle = T("export.interpretation"),
                    interpretations = BuildInterpretations(filtered),
                    riskScore = Math.Min(100, CriticalCount * 8 + AlertCount * 3 + FallCount * 10),
                    riskLevel = CriticalCount > 0 ? "HIGH" : AlertCount > 0 ? "MEDIUM" : "LOW",
                    riskScoreLabel = T("export.riskScore"),
                    riskBreakdown = Array.Empty<object>(),
                    riskBreakdownTitle = T("export.riskBreakdown"),
                    dataConfidence = _exportDistinctDays >= 7 ? 100 : Math.Round(_exportDistinctDays / 7.0 * 100),
                    dataConfidenceNote = $"{_exportDistinctDays} {T("export.daysWithData")}",
                    dataConfidenceLabel = T("export.dataConfidence"),
                    topConcerns = KeyMoments.Take(3).Select((m, i) => new { rank = i + 1, text = m.Title, severity = m.Severity }).ToArray(),
                    topConcernsTitle = T("export.topConcerns"),
                    conclusionSectionTitle = T("export.conclusion"),
                    conclusion = new[] { GetSummaryText() },
                    hDate = T("export.date"),
                    hPulse = T("selected.heartRate"),
                    hTemp = T("selected.temperature"),
                    hSpo2 = "SpO2",
                    hActivity = T("export.activity"),
                    hFall = T("export.fall"),
                    hReason = T("export.reason"),
                    hWeek = T("export.week"),
                    hCount = T("export.count"),
                    patientName,
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
            }
        }

        private object[] BuildExportSummary(List<MeasurementResponseDTO> measurements)
        {
            if (!measurements.Any()) return Array.Empty<object>();

            return new object[]
            {
                new { metric = T("selected.heartRate"), avg = $"{measurements.Average(m => m.Pulse):F1}", min = $"{measurements.Min(m => m.Pulse):F0}", max = $"{measurements.Max(m => m.Pulse):F0}", stdDev = $"{StdDev(measurements.Select(m => (double)m.Pulse).ToList()):F1}" },
                new { metric = T("selected.temperature"), avg = $"{measurements.Average(m => m.Temperature):F1}", min = $"{measurements.Min(m => m.Temperature):F1}", max = $"{measurements.Max(m => m.Temperature):F1}", stdDev = $"{StdDev(measurements.Select(m => m.Temperature).ToList()):F1}" },
                new { metric = "SpO2", avg = $"{measurements.Average(m => m.SpO2):F1}", min = $"{measurements.Min(m => m.SpO2):F0}", max = $"{measurements.Max(m => m.SpO2):F0}", stdDev = $"{StdDev(measurements.Select(m => (double)m.SpO2).ToList()):F1}" }
            };
        }

        private object[] BuildWeeklyBreakdown(List<MeasurementResponseDTO> measurements) => measurements
            .GroupBy(m =>
            {
                var date = m.CreatedAt.ToLocalTime().Date;
                var diff = ((int)date.DayOfWeek - (int)_firstDayOfWeek + 7) % 7;
                return date.AddDays(-diff);
            })
            .Select(g => new
            {
                week = $"{g.Key:dd MMM} - {g.Key.AddDays(6):dd MMM}",
                count = g.Count(),
                avgPulse = $"{g.Average(m => m.Pulse):F1}",
                avgTemp = $"{g.Average(m => m.Temperature):F1}",
                avgSpo2 = $"{g.Average(m => m.SpO2):F1}"
            })
            .ToArray();

        private object[] BuildDailyBreakdown(List<MeasurementResponseDTO> measurements) => measurements
            .GroupBy(m => m.CreatedAt.ToLocalTime().Date)
            .Select(g => new
            {
                date = g.Key.ToString("dd MMM yyyy"),
                count = g.Count(),
                avgPulse = $"{g.Average(m => m.Pulse):F1}",
                avgTemp = $"{g.Average(m => m.Temperature):F1}",
                avgSpo2 = $"{g.Average(m => m.SpO2):F1}"
            })
            .ToArray();

        private object ToExportEvent(MeasurementResponseDTO m) => new
        {
            date = m.CreatedAt.ToLocalTime().ToString("dd MMM yyyy, HH:mm"),
            pulse = $"{m.Pulse:F0}",
            temp = $"{m.Temperature:F1}",
            spo2 = $"{m.SpO2:F0}",
            reason = GetStatusLabel(GetStatus(m))
        };

        private object[] BuildInterpretations(List<MeasurementResponseDTO> measurements)
        {
            if (!measurements.Any()) return Array.Empty<object>();

            var status = measurements.Any(m => GetStatus(m) == "critical")
                ? "high"
                : measurements.Any(m => GetStatus(m) == "alert") ? "medium" : "low";

            return new object[]
            {
                new { text = GetSummaryText(), plain = GetSummaryText(), severity = status }
            };
        }

        private static double StdDev(List<double> values)
        {
            if (values.Count <= 1) return 0;
            var average = values.Average();
            var sumSq = values.Sum(v => (v - average) * (v - average));
            return Math.Sqrt(sumSq / (values.Count - 1));
        }

        private sealed record KeyMoment(string Title, string Description, DateTime Time, string Severity);

        private sealed class ChartDataPoint
        {
            public string Day { get; set; } = string.Empty;
            public double ActualValue { get; set; }
            public bool HasData { get; set; } = true;
            public double XFraction { get; set; } = -1;
        }

        private string? GetQueryParam(string name)
        {
            try
            {
                var uri = new Uri(Navigation.Uri);
                var query = uri.Query;
                if (string.IsNullOrWhiteSpace(query)) return null;

                if (query.StartsWith("?")) query = query[1..];
                var parts = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
                foreach (var p in parts)
                {
                    var kv = p.Split('=', 2);
                    if (kv.Length == 0) continue;
                    var key = Uri.UnescapeDataString(kv[0]);
                    if (!key.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                    return kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : string.Empty;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private string GetInitials(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "?";

            var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            return string.Join("", parts.Select(p => char.ToUpper(p[0])));
        }
    }
}
