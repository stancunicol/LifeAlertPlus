using System.ComponentModel.DataAnnotations;

namespace LifeAlertPlus.Shared.DTOs.Requests.Monitored
{
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
        public int? UpdateFrequency { get; set; }
    }
}
