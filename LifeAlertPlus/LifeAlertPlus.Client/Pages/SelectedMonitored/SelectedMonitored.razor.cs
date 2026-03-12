using Microsoft.AspNetCore.Components;
using LifeAlertPlus.Client.Services;

namespace LifeAlertPlus.Client.Pages.SelectedMonitored
{
    public partial class SelectedMonitored : ComponentBase
    {
        [Parameter]
        public Guid PersonId { get; set; }

        [Inject]
        private NavigationManager NavigationManager { get; set; } = default!;

        [Inject]
        private MonitoredService MonitoredService { get; set; } = default!;

        [Inject]
        private TokenParserService TokenParserService { get; set; } = default!;

        private PersonDetail? Person { get; set; }
        private bool IsLoading { get; set; } = true;
        private string? LoadError { get; set; }

        private List<ChartDataPoint> HeartRateHistory { get; set; } = new();
        private List<BPChartDataPoint> BloodPressureHistory { get; set; } = new();
        private List<Alert> RecentAlerts { get; set; } = new();
        private List<Measurement> RecentMeasurements { get; set; } = new();
        private string UserFullName = "";
        private string ProfilePictureUrl = "";

        private async Task LoadPersonDataAsync()
        {
            IsLoading = true;
            LoadError = null;

            var monitored = await MonitoredService.GetMonitoredPersonByIdAsync(PersonId);
            if (monitored == null)
            {
                Person = null;
                LoadError = "Monitored person not found.";
                IsLoading = false;
                return;
            }

            Person = new PersonDetail
            {
                Id = monitored.Id,
                Name = $"{monitored.FirstName} {monitored.LastName}".Trim(),
                Age = GetAge(monitored.Birthdate),
                Relationship = "Family",
                HeartRate = 0,
                BloodPressure = "N/A",
                Temperature = 0,
                Glucose = 0,
                Status = "OK",
                LastUpdate = monitored.UpdatedAt?.ToLocalTime().ToString("g") ?? monitored.CreatedAt.ToLocalTime().ToString("g"),
                Location = string.IsNullOrWhiteSpace(monitored.Address) ? "N/A" : monitored.Address,
                Phone = "N/A"
            };

            IsLoading = false;
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

        private void LoadChartData()
        {
            HeartRateHistory = new List<ChartDataPoint>
            {
                new ChartDataPoint { Day = "Lun", Value = 65 },
                new ChartDataPoint { Day = "Mar", Value = 72 },
                new ChartDataPoint { Day = "Mie", Value = 68 },
                new ChartDataPoint { Day = "Joi", Value = 75 },
                new ChartDataPoint { Day = "Vin", Value = 70 },
                new ChartDataPoint { Day = "Sâm", Value = 78 },
                new ChartDataPoint { Day = "Dum", Value = 73 }
            };

            BloodPressureHistory = new List<BPChartDataPoint>
            {
                new BPChartDataPoint { Day = "Lun", Systolic = 70, Diastolic = 45 },
                new BPChartDataPoint { Day = "Mar", Systolic = 68, Diastolic = 43 },
                new BPChartDataPoint { Day = "Mie", Systolic = 72, Diastolic = 46 },
                new BPChartDataPoint { Day = "Joi", Systolic = 75, Diastolic = 48 },
                new BPChartDataPoint { Day = "Vin", Systolic = 73, Diastolic = 47 },
                new BPChartDataPoint { Day = "Sâm", Systolic = 71, Diastolic = 45 },
                new BPChartDataPoint { Day = "Dum", Systolic = 74, Diastolic = 47 }
            };
        }

        private void LoadRecentAlerts()
        {
            RecentAlerts = new List<Alert>
            {
                new Alert { Severity = "Warning", Title = "Puls Crescut", Description = "Pulsul a depășit 95 bpm", Time = "Acum 2h" },
                new Alert { Severity = "Info", Title = "Măsurătoare Nouă", Description = "Tensiune înregistrată: 120/80", Time = "Acum 3h" },
                new Alert { Severity = "Critical", Title = "Temperatură Ridicată", Description = "Temperatură de 37.8°C detectată", Time = "Ieri" }
            };
        }

        private void LoadRecentMeasurements()
        {
            RecentMeasurements = new List<Measurement>
            {
                new Measurement { Icon = "❤️", Type = "Puls", Value = "78 bpm", Time = "Acum 2h" },
                new Measurement { Icon = "🩸", Type = "Tensiune", Value = "120/80 mmHg", Time = "Acum 2h" },
                new Measurement { Icon = "🌡️", Type = "Temperatură", Value = "36.5°C", Time = "Acum 3h" },
                new Measurement { Icon = "🍬", Type = "Glicemie", Value = "95 mg/dL", Time = "Acum 4h" }
            };
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
                "critical" => "Alert",
                "warning" => "Atenție",
                "ok" => "Stabil",
                _ => "Necunoscut"
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
            LoadChartData();
            LoadRecentAlerts();
            LoadRecentMeasurements();
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
                return "Sub normal";
            if (temp > 37.5)
                return "Peste normal";
            return "Normal";
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
            public string BloodPressure { get; set; } = string.Empty;
            public double Temperature { get; set; }
            public int Glucose { get; set; }
            public string Status { get; set; } = string.Empty;
            public string LastUpdate { get; set; } = string.Empty;
            public string Location { get; set; } = string.Empty;
            public string Phone { get; set; } = string.Empty;
        }

        public class ChartDataPoint
        {
            public string Day { get; set; } = string.Empty;
            public int Value { get; set; }
        }

        public class BPChartDataPoint
        {
            public string Day { get; set; } = string.Empty;
            public int Systolic { get; set; }
            public int Diastolic { get; set; }
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
