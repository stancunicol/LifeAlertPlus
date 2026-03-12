namespace LifeAlertPlus.Shared.DTOs.Requests.Monitored
{
    public class MonitorCreateRequestDTO
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public DateTime Birthdate { get; set; }
        public string Gender { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string DeviceSerialNumber { get; set; } = string.Empty;
        public string Relationship { get; set; } = string.Empty;
    }
}