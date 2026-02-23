namespace LifeAlertPlus.Shared.DTOs.Requests.Monitored
{
    public class MonitorAddRequestDTO
    {
        public MonitorCreateRequestDTO MonitoredPerson { get; set; } = new MonitorCreateRequestDTO();
        public string CurrentUserEmail { get; set; } = string.Empty;
    }
}