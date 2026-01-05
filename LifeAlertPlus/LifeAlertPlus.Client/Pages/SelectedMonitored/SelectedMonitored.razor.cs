using Microsoft.AspNetCore.Components;

namespace LifeAlertPlus.Client.Pages.SelectedMonitored
{
    public partial class SelectedMonitored : ComponentBase
    {
        [Parameter]
        public int PersonId { get; set; }

        [Inject]
        private NavigationManager NavigationManager { get; set; } = default!;

        private PersonDetail? Person { get; set; }

        private List<ChartDataPoint> HeartRateHistory { get; set; } = new();
        private List<BPChartDataPoint> BloodPressureHistory { get; set; } = new();
        private List<Alert> RecentAlerts { get; set; } = new();
        private List<Measurement> RecentMeasurements { get; set; } = new();
        private string UserFullName = "";
        private string ProfilePictureUrl = "";

        protected override void OnInitialized()
        {
            LoadPersonData();
            LoadChartData();
            LoadRecentAlerts();
            LoadRecentMeasurements();
        }

        private void LoadPersonData()
        {
            var allPeople = new List<PersonDetail>
            {
                new PersonDetail { Id = 1, Name = "Elena Popescu", Age = 76, Relationship = "Mother", HeartRate = 78, BloodPressure = "120/80", Temperature = 36.5, Glucose = 95, Status = "OK", LastUpdate = "Acum 2h", Location = "București, Romania", Phone = "+40 721 234 567" },
                new PersonDetail { Id = 2, Name = "Ion Popa", Age = 82, Relationship = "Father", HeartRate = 95, BloodPressure = "138/88", Temperature = 36.9, Glucose = 118, Status = "Warning", LastUpdate = "Acum 1h", Location = "Cluj-Napoca, Romania", Phone = "+40 732 345 678" },
                new PersonDetail { Id = 3, Name = "Maria Ionescu", Age = 75, Relationship = "Aunt", HeartRate = 105, BloodPressure = "145/89", Temperature = 37.1, Glucose = 145, Status = "Critical", LastUpdate = "Acum 30min", Location = "Timișoara, Romania", Phone = "+40 743 456 789" },
                new PersonDetail { Id = 4, Name = "Vasile Dumitrescu", Age = 70, Relationship = "Uncle", HeartRate = 82, BloodPressure = "125/82", Temperature = 36.7, Glucose = 102, Status = "OK", LastUpdate = "Acum 1h", Location = "Iași, Romania", Phone = "+40 754 567 890" },
                new PersonDetail { Id = 5, Name = "Ana Marin", Age = 63, Relationship = "Mother-in-law", HeartRate = 78, BloodPressure = "118/78", Temperature = 36.5, Glucose = 88, Status = "OK", LastUpdate = "Acum 3h", Location = "Constanța, Romania", Phone = "+40 765 678 901" },
                new PersonDetail { Id = 6, Name = "Gheorghe Stan", Age = 79, Relationship = "Grandfather", HeartRate = 92, BloodPressure = "142/86", Temperature = 37.0, Glucose = 125, Status = "Warning", LastUpdate = "Acum 45min", Location = "Brașov, Romania", Phone = "+40 776 789 012" },
                new PersonDetail { Id = 7, Name = "Ioana Radu", Age = 68, Relationship = "Grandmother", HeartRate = 75, BloodPressure = "115/75", Temperature = 36.4, Glucose = 92, Status = "OK", LastUpdate = "Acum 2h", Location = "Sibiu, Romania", Phone = "+40 787 890 123" },
                new PersonDetail { Id = 8, Name = "Mihai Petre", Age = 85, Relationship = "Neighbor", HeartRate = 110, BloodPressure = "150/92", Temperature = 37.3, Glucose = 152, Status = "Critical", LastUpdate = "Acum 15min", Location = "București, Romania", Phone = "+40 798 901 234" }
            };

            Person = allPeople.FirstOrDefault(p => p.Id == PersonId);
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
            var token = await JSRuntime.InvokeAsync<string>("localStorage.getItem", new object[] { "authToken" });
            if (!string.IsNullOrEmpty(token))
            {
                var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                var jsonToken = handler.ReadJwtToken(token);
                var firstName = jsonToken?.Claims?.FirstOrDefault(x => x.Type == "firstName")?.Value ?? "";
                var lastName = jsonToken?.Claims?.FirstOrDefault(x => x.Type == "lastName")?.Value ?? "";
                var profilePictureUrl = jsonToken?.Claims?.FirstOrDefault(x => x.Type == "profilePictureUrl")?.Value ?? "";
                UserFullName = $"{firstName} {lastName}".Trim();
                ProfilePictureUrl = profilePictureUrl;
            }
            else
            {
                UserFullName = "User";
            }
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
            public int Id { get; set; }
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
