namespace LifeAlertPlus.Shared.DTOs.Requests.Monitored
{
    public class MonitorCreateRequestDTO
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public DateTime Birthdate { get; set; }
        public string Gender { get; set; }
        public string Address { get; set; }
        public string DeviceSerialNumber { get; set; }
        public string Relationship { get; set; }
    }
}