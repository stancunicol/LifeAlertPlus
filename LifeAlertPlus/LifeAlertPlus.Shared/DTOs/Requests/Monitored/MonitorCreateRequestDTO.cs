using System.ComponentModel.DataAnnotations;

namespace LifeAlertPlus.Shared.DTOs.Requests.Monitored
{
    // Datele de bază ale unei persoane monitorizate noi — câmp imbricat în MonitorAddRequestDTO (creare prin UI)
    // și folosit direct de MonitoredService.AddMonitoredPersonAsync (Application). Atributele [Required]
    // sunt validate automat de ASP.NET Core model binding înainte ca request-ul să ajungă în controller.
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
    }
}