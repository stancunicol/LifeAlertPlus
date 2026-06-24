using System.ComponentModel.DataAnnotations;

namespace LifeAlertPlus.Shared.DTOs.Requests.Monitored
{
    // Server: primit de MonitoredController.UpdateMonitoredPerson (PUT /api/monitored/{id}) — permite
    // editarea datelor personale ȘI a pragurilor vitale/politicii de retenție ale unui pacient existent
    // (toate pragurile sunt nullable — null = nu se modifică valoarea curentă din DB).
    // Client: construit din formularul de editare al pacientului (ex: SelectedMonitored.razor.cs),
    // trimis prin MonitoredApiClient.
    public class MonitorUpdateRequestDTO
    {
        [Required]
        public string FirstName { get; set; } = string.Empty;
        [Required]
        public string LastName { get; set; } = string.Empty;
        public DateTime? Birthdate { get; set; }
        [Required]
        public string Gender { get; set; } = string.Empty;
        [Required]
        public string Address { get; set; } = string.Empty;
        [Required]
        public string DeviceSerialNumber { get; set; } = string.Empty;
        public int? MinHeartRate { get; set; }
        public int? MaxHeartRate { get; set; }
        public double? MinTemperature { get; set; }
        public double? MaxTemperature { get; set; }
        public int? MinSpO2 { get; set; }
        public int? MaxSpO2 { get; set; }
        public int? UpdateFrequency { get; set; }
        public int? DataRetentionDays { get; set; }
        public int? ArchiveRetentionDays { get; set; }
    }
}
