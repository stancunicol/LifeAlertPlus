using Microsoft.AspNetCore.Components;

namespace LifeAlertPlus.Client.Pages.History;

public partial class HistoryPage : ComponentBase
{
    private string CurrentUser = "John Doe";
    private string SelectedPerson = "All";
    private string SelectedMeasurementType = "All";
    private string SelectedTimePeriod = "Week";

    private List<string> People = new()
    {
        "Elena Popescu",
        "Ion Popa",
        "Maria Ionescu",
        "Vasile Dumitrescu",
        "Ana Marin"
    };

    private List<Measurement> AllMeasurements = new()
    {
        new Measurement { DateTime = "24 Nov 2025, 14:30", PersonName = "Maria Ionescu", Type = "HeartRate", Value = "105 bpm", Status = "Abnormal", Notes = "Elevated, needs attention" },
        new Measurement { DateTime = "24 Nov 2025, 14:15", PersonName = "Maria Ionescu", Type = "BloodPressure", Value = "145/89 mmHg", Status = "Abnormal", Notes = "High blood pressure" },
        new Measurement { DateTime = "24 Nov 2025, 13:00", PersonName = "Elena Popescu", Type = "HeartRate", Value = "78 bpm", Status = "Normal", Notes = "Regular check" },
        new Measurement { DateTime = "24 Nov 2025, 12:45", PersonName = "Elena Popescu", Type = "Temperature", Value = "36.5°C", Status = "Normal", Notes = "All good" },
        new Measurement { DateTime = "24 Nov 2025, 11:30", PersonName = "Ion Popa", Type = "HeartRate", Value = "95 bpm", Status = "Warning", Notes = "Slightly elevated" },
        new Measurement { DateTime = "24 Nov 2025, 11:15", PersonName = "Ion Popa", Type = "BloodPressure", Value = "138/88 mmHg", Status = "Warning", Notes = "Monitor closely" },
        new Measurement { DateTime = "24 Nov 2025, 10:00", PersonName = "Vasile Dumitrescu", Type = "HeartRate", Value = "82 bpm", Status = "Normal", Notes = "Stable" },
        new Measurement { DateTime = "24 Nov 2025, 09:30", PersonName = "Vasile Dumitrescu", Type = "Temperature", Value = "36.7°C", Status = "Normal", Notes = "Morning check" },
        new Measurement { DateTime = "24 Nov 2025, 09:00", PersonName = "Ana Marin", Type = "HeartRate", Value = "78 bpm", Status = "Normal", Notes = "Daily measurement" },
        new Measurement { DateTime = "24 Nov 2025, 08:30", PersonName = "Ana Marin", Type = "BloodPressure", Value = "118/78 mmHg", Status = "Normal", Notes = "Excellent" },
        new Measurement { DateTime = "23 Nov 2025, 18:00", PersonName = "Maria Ionescu", Type = "HeartRate", Value = "102 bpm", Status = "Abnormal", Notes = "Evening spike" },
        new Measurement { DateTime = "23 Nov 2025, 17:30", PersonName = "Elena Popescu", Type = "HeartRate", Value = "75 bpm", Status = "Normal", Notes = "Evening check" },
        new Measurement { DateTime = "23 Nov 2025, 15:00", PersonName = "Ion Popa", Type = "Temperature", Value = "36.9°C", Status = "Normal", Notes = "Afternoon" },
        new Measurement { DateTime = "23 Nov 2025, 14:00", PersonName = "Vasile Dumitrescu", Type = "BloodPressure", Value = "125/82 mmHg", Status = "Normal", Notes = "Stable reading" },
        new Measurement { DateTime = "23 Nov 2025, 12:00", PersonName = "Ana Marin", Type = "Temperature", Value = "36.5°C", Status = "Normal", Notes = "Midday check" },
        new Measurement { DateTime = "23 Nov 2025, 10:30", PersonName = "Maria Ionescu", Type = "BloodPressure", Value = "148/90 mmHg", Status = "Abnormal", Notes = "Consistently high" },
        new Measurement { DateTime = "22 Nov 2025, 16:00", PersonName = "Elena Popescu", Type = "BloodPressure", Value = "120/80 mmHg", Status = "Normal", Notes = "Perfect reading" },
        new Measurement { DateTime = "22 Nov 2025, 14:30", PersonName = "Ion Popa", Type = "HeartRate", Value = "92 bpm", Status = "Warning", Notes = "Slightly high" },
        new Measurement { DateTime = "22 Nov 2025, 11:00", PersonName = "Vasile Dumitrescu", Type = "Temperature", Value = "36.6°C", Status = "Normal", Notes = "Regular" },
        new Measurement { DateTime = "22 Nov 2025, 09:00", PersonName = "Ana Marin", Type = "HeartRate", Value = "76 bpm", Status = "Normal", Notes = "Morning measurement" }
    };

    private List<Measurement> FilteredMeasurements => AllMeasurements;

    private int AbnormalCount => AllMeasurements.Count(m => m.Status == "Abnormal");
    private int AverageHeartRate
    {
        get
        {
            var heartRates = AllMeasurements
                .Where(m => m.Type == "HeartRate")
                .Select(m => int.Parse(m.Value.Split(' ')[0]))
                .ToList();
            return heartRates.Any() ? (int)heartRates.Average() : 0;
        }
    }
    private string AverageTemperature
    {
        get
        {
            var temps = AllMeasurements
                .Where(m => m.Type == "Temperature")
                .Select(m => double.Parse(m.Value.Replace("°C", "")))
                .ToList();
            return temps.Any() ? temps.Average().ToString("F1") : "0.0";
        }
    }

    private void ApplyFilters()
    {
        StateHasChanged();
    }

    private string GetInitials(string name)
    {
        var parts = name.Split(' ');
        if (parts.Length >= 2)
            return $"{parts[0][0]}{parts[1][0]}".ToUpper();
        return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpper();
    }

    private string GetRowClass(string status)
    {
        return status.ToLower() switch
        {
            "abnormal" => "row-abnormal",
            "warning" => "row-warning",
            "normal" => "row-normal",
            _ => ""
        };
    }

    private string GetTypeIcon(string type)
    {
        return type switch
        {
            "HeartRate" => "❤️",
            "BloodPressure" => "🩸",
            "Temperature" => "🌡️",
            _ => "📊"
        };
    }

    public class Measurement
    {
        public string DateTime { get; set; } = string.Empty;
        public string PersonName { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
    }
}