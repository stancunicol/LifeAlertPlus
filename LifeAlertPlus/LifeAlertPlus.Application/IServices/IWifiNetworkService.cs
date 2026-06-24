using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Application.IServices
{
    // Interfață pentru gestionarea rețelelor WiFi ale dispozitivului ESP32
    // ESP32 poate stoca până la MaxNetworksPerDevice rețele (trimise la dispozitiv la configurare)
    public interface IWifiNetworkService
    {
        public const int MaxNetworksPerDevice = 3; // Limita de rețele WiFi per dispozitiv (ESP32 are memorie limitată)

        Task<IEnumerable<WifiNetwork>> GetByMonitoredIdAsync(Guid monitoredId);                                          // Returnează toate rețelele WiFi ale persoanei monitorizate
        Task<IEnumerable<WifiNetwork>> GetByDeviceSerialAsync(string serial);                                            // Returnează rețelele după numărul de serie al dispozitivului ESP32
        Task<WifiNetwork?> GetByIdAsync(Guid id);                                                                        // Returnează o rețea WiFi după ID
        Task<(bool Success, string? ErrorKey, WifiNetwork? Network)> AddAsync(Guid monitoredId, string ssid, string password); // Adaugă o rețea nouă (parola e stocată ca text simplu); returnează eroare dacă limita e atinsă
        Task<bool> DeleteAsync(Guid id);                                                                                 // Șterge o rețea WiFi după ID
    }
}
