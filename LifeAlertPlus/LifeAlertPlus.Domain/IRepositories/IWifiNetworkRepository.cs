using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Domain.IRepositories
{
    // Interfață repository pentru tabela WifiNetworks (rețelele WiFi salvate pentru dispozitivul ESP32)
    // NOTĂ: parola este stocată ca text simplu (nu există criptare/value converter configurat)
    public interface IWifiNetworkRepository
    {
        Task<IEnumerable<WifiNetwork>> GetByMonitoredIdAsync(Guid monitoredId); // Returnează toate rețelele asociate persoanei monitorizate
        Task<IEnumerable<WifiNetwork>> GetByDeviceSerialAsync(string serial);   // Returnează rețelele după seria dispozitivului (ESP32 cere la boot)
        Task<WifiNetwork?> GetByIdAsync(Guid id);                               // Returnează o rețea după ID
        Task<int> CountByMonitoredIdAsync(Guid monitoredId);                    // Numărul de rețele salvate (verificare limită MaxNetworksPerDevice=3)
        Task AddAsync(WifiNetwork network);                                      // Inserează o rețea nouă
        Task DeleteAsync(WifiNetwork network);                                   // Șterge o rețea WiFi
    }
}
