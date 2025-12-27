using Microsoft.AspNetCore.Components;

namespace LifeAlertPlus.Client.Pages.Dashboard;

public partial class DashboardPage : ComponentBase
{
    private string UserFullName = "";
        protected override async Task OnInitializedAsync()
        {
            var token = await JSRuntime.InvokeAsync<string>("localStorage.getItem", new object[] { "authToken" });
            if (!string.IsNullOrEmpty(token))
            {
                var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                var jsonToken = handler.ReadJwtToken(token);
                var firstName = jsonToken?.Claims?.FirstOrDefault(x => x.Type == "firstName")?.Value ?? "";
                var lastName = jsonToken?.Claims?.FirstOrDefault(x => x.Type == "lastName")?.Value ?? "";
                UserFullName = $"{firstName} {lastName}".Trim();
            }
            else
            {
                UserFullName = "User";
            }
        }
    private int ActiveAlerts = 3;
    private int StableCount = 5;
    private int TodayMeasurements = 24;

    private List<MonitoredPerson> MonitoredPeople = new()
    {
        new MonitoredPerson
        {
            Name = "Elena Popescu",
            Age = 76,
            HeartRate = 78,
            BloodPressure = "N/A",
            Temperature = 36.5,
            Status = "OK",
            LastUpdate = "2h"
        },
        new MonitoredPerson
        {
            Name = "Ion Popa",
            Age = 82,
            HeartRate = 95,
            BloodPressure = "N/A",
            Temperature = 36.9,
            Status = "Warning",
            LastUpdate = "1h"
        },
        new MonitoredPerson
        {
            Name = "Maria Ionescu",
            Age = 75,
            HeartRate = 105,
            BloodPressure = "145/89",
            Temperature = 37.1,
            Status = "Critical",
            LastUpdate = "3h"
        },
        new MonitoredPerson
        {
            Name = "Vasile Dumitrescu",
            Age = 70,
            HeartRate = 82,
            BloodPressure = "125/82",
            Temperature = 36.7,
            Status = "OK",
            LastUpdate = "30min"
        },
        new MonitoredPerson
        {
            Name = "Ana Marin",
            Age = 63,
            HeartRate = 78,
            BloodPressure = "118/78",
            Temperature = 36.5,
            Status = "OK",
            LastUpdate = "1h"
        }
    };

    private List<Alert> RecentAlerts = new()
    {
        new Alert
        {
            Type = "Critical",
            Title = "High Heart Rate Alert",
            Description = "Maria Popescu's heart rate exceeded 140 bpm",
            Time = "5 minutes ago"
        },
        new Alert
        {
            Type = "Warning",
            Title = "Blood Pressure Warning",
            Description = "Ion Ionescu's blood pressure is slightly elevated",
            Time = "1 hour ago"
        },
        new Alert
        {
            Type = "Info",
            Title = "Measurement Completed",
            Description = "Elena Georgescu completed daily health check",
            Time = "2 hours ago"
        },
        new Alert
        {
            Type = "Critical",
            Title = "Missed Measurement",
            Description = "Maria Popescu missed scheduled measurement",
            Time = "3 hours ago"
        }
    };

    private string GetInitials(string name)
    {
        var parts = name.Split(' ');
        if (parts.Length >= 2)
            return $"{parts[0][0]}{parts[1][0]}".ToUpper();
        return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpper();
    }

    private string GetStatusClass(string status)
    {
        return status.ToLower() switch
        {
            "critical" => "status-critical",
            "warning" => "status-warning",
            "ok" => "status-ok",
            _ => ""
        };
    }

    private string GetAlertIcon(string type)
    {
        return type.ToLower() switch
        {
            "critical" => "🚨",
            "warning" => "⚠️",
            "info" => "ℹ️",
            _ => "📋"
        };
    }

    private string GetStatusText(string status)
    {
        return status.ToLower() switch
        {
            "critical" => "Alert",
            "warning" => "Check needed",
            "ok" => "Stable",
            _ => "Unknown"
        };
    }

    public class MonitoredPerson
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
        public int HeartRate { get; set; }
        public string BloodPressure { get; set; } = string.Empty;
        public double Temperature { get; set; }
        public string Status { get; set; } = string.Empty;
        public string LastUpdate { get; set; } = string.Empty;
    }

    public class Alert
    {
        public string Type { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Time { get; set; } = string.Empty;
    }
}