namespace LifeAlertPlus.Shared.DTOs.Responses.Wifi
{
    // Server: returnat de WifiController (GetByMonitored, Add) — exclude intenționat parola din răspuns
    // (WifiNetworkRequestDTO are Password, acesta nu) ca să nu se scurgă către clientul web, deși
    // backend-ul o stochează ca text simplu, nu criptat (vezi WifiNetworkRepository).
    // Client: consumat de WifiApiClient.cs, afișat în UI-ul de configurare rețele WiFi al pacientului.
    public class WifiNetworkResponseDTO
    {
        public Guid Id { get; set; }
        public string Ssid { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
