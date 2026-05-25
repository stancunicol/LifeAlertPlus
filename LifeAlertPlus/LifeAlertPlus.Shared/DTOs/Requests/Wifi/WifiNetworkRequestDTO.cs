namespace LifeAlertPlus.Shared.DTOs.Requests.Wifi
{
    public class WifiNetworkRequestDTO
    {
        public Guid IdMonitored { get; set; }
        public string Ssid { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
