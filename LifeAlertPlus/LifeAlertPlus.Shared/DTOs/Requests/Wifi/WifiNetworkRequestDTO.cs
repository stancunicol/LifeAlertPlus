namespace LifeAlertPlus.Shared.DTOs.Requests.Wifi
{
    // Server: primit de WifiController.Add (POST /api/wifi) → WifiNetworkService.AddAsync validează
    // SSID/parolă și limita de 3 rețele per dispozitiv. NOTĂ: parola e stocată ca text simplu în DB
    // (nu există criptare configurată — vezi WifiNetworkRepository).
    // Client: construit în WifiApiClient.cs (linia ~39), folosit din UI-ul de configurare WiFi al pacientului.
    public class WifiNetworkRequestDTO
    {
        public Guid IdMonitored { get; set; }
        public string Ssid { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
