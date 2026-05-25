namespace LifeAlertPlus.Domain.Entities
{
    public class WifiNetwork
    {
        public Guid Id { get; set; }
        public Guid IdMonitored { get; set; }
        public string Ssid { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }

        public Monitored Monitored { get; set; } = default!;
    }
}
