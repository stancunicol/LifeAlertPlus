using Microsoft.AspNetCore.Components;

namespace LifeAlertPlus.Client.Pages.Monitored;

public partial class MonitoredPage : ComponentBase
{
    private string CurrentUser = "John Doe";
    private string FilterStatus = "All";
    private bool ShowAddPersonModal = false;

    private List<MonitoredPersonDetailed> AllPeople = new()
    {
        new MonitoredPersonDetailed
        {
            Name = "Elena Popescu",
            Age = 76,
            Relationship = "Mother",
            HeartRate = 78,
            BloodPressure = "120/80",
            Temperature = 36.5,
            Status = "OK",
            LastUpdate = "Acum 2h",
            Location = "Bucuresti, Romania",
            Phone = "+40 721 234 567"
        },
        new MonitoredPersonDetailed
        {
            Name = "Ion Popa",
            Age = 82,
            Relationship = "Father",
            HeartRate = 95,
            BloodPressure = "138/88",
            Temperature = 36.9,
            Status = "Warning",
            LastUpdate = "Acum 1h",
            Location = "Cluj-Napoca, Romania",
            Phone = "+40 732 345 678"
        },
        new MonitoredPersonDetailed
        {
            Name = "Maria Ionescu",
            Age = 75,
            Relationship = "Aunt",
            HeartRate = 105,
            BloodPressure = "145/89",
            Temperature = 37.1,
            Status = "Critical",
            LastUpdate = "Acum 30min",
            Location = "Timisoara, Romania",
            Phone = "+40 743 456 789"
        },
        new MonitoredPersonDetailed
        {
            Name = "Vasile Dumitrescu",
            Age = 70,
            Relationship = "Uncle",
            HeartRate = 82,
            BloodPressure = "125/82",
            Temperature = 36.7,
            Status = "OK",
            LastUpdate = "Acum 1h",
            Location = "Iasi, Romania",
            Phone = "+40 754 567 890"
        },
        new MonitoredPersonDetailed
        {
            Name = "Ana Marin",
            Age = 63,
            Relationship = "Mother-in-law",
            HeartRate = 78,
            BloodPressure = "118/78",
            Temperature = 36.5,
            Status = "OK",
            LastUpdate = "Acum 3h",
            Location = "Constanta, Romania",
            Phone = "+40 765 678 901"
        },
        new MonitoredPersonDetailed
        {
            Name = "Gheorghe Stan",
            Age = 79,
            Relationship = "Grandfather",
            HeartRate = 92,
            BloodPressure = "142/86",
            Temperature = 37.0,
            Status = "Warning",
            LastUpdate = "Acum 45min",
            Location = "Brasov, Romania",
            Phone = "+40 776 789 012"
        },
        new MonitoredPersonDetailed
        {
            Name = "Ioana Radu",
            Age = 68,
            Relationship = "Grandmother",
            HeartRate = 75,
            BloodPressure = "115/75",
            Temperature = 36.4,
            Status = "OK",
            LastUpdate = "Acum 2h",
            Location = "Sibiu, Romania",
            Phone = "+40 787 890 123"
        },
        new MonitoredPersonDetailed
        {
            Name = "Mihai Petre",
            Age = 85,
            Relationship = "Neighbor",
            HeartRate = 110,
            BloodPressure = "150/92",
            Temperature = 37.3,
            Status = "Critical",
            LastUpdate = "Acum 15min",
            Location = "Bucuresti, Romania",
            Phone = "+40 798 901 234"
        }
    };

    private List<MonitoredPersonDetailed> FilteredPeople =>
        FilterStatus == "All" 
            ? AllPeople 
            : AllPeople.Where(p => p.Status.Equals(FilterStatus, StringComparison.OrdinalIgnoreCase)).ToList();

    private int CriticalCount => AllPeople.Count(p => p.Status.Equals("Critical", StringComparison.OrdinalIgnoreCase));
    private int WarningCount => AllPeople.Count(p => p.Status.Equals("Warning", StringComparison.OrdinalIgnoreCase));
    private int StableCount => AllPeople.Count(p => p.Status.Equals("OK", StringComparison.OrdinalIgnoreCase));

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

    private void OpenAddPersonModal()
    {
        ShowAddPersonModal = true;
    }

    private void CloseAddPersonModal()
    {
        ShowAddPersonModal = false;
    }

    public class MonitoredPersonDetailed
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
        public string Relationship { get; set; } = string.Empty;
        public int HeartRate { get; set; }
        public string BloodPressure { get; set; } = string.Empty;
        public double Temperature { get; set; }
        public string Status { get; set; } = string.Empty;
        public string LastUpdate { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
    }
}
