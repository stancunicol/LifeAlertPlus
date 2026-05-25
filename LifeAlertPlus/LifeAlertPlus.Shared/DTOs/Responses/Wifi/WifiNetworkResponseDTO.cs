namespace LifeAlertPlus.Shared.DTOs.Responses.Wifi
{
    public class WifiNetworkResponseDTO
    {
        public Guid Id { get; set; }
        public string Ssid { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
