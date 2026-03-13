using System.ComponentModel.DataAnnotations;

namespace LifeAlertPlus.Shared.DTOs.Requests.Monitored
{
    public class MonitorCreateRequestDTO
    {
        [Required]
        public string FirstName { get; set; } = string.Empty;
        [Required]
        public string LastName { get; set; } = string.Empty;
        [Required]
        public DateTime Birthdate { get; set; }
        [Required]
        public string Gender { get; set; } = string.Empty;
        [Required]
        public string Address { get; set; } = string.Empty;
        [Required]
        public string DeviceSerialNumber { get; set; } = string.Empty;
        [Required]
        public string Relationship { get; set; } = string.Empty;
        [Required]
        public bool IsActive { get; set; }
    }
}