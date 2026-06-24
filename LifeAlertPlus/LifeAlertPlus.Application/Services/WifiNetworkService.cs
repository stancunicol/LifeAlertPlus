using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;

namespace LifeAlertPlus.Application.Services
{
    // Serviciu pentru gestionarea rețelelor WiFi ale ESP32
    // NOTĂ: parola este stocată ca text simplu în DB (nu există criptare configurată)
    public class WifiNetworkService : IWifiNetworkService
    {
        private readonly IWifiNetworkRepository _repo;

        public WifiNetworkService(IWifiNetworkRepository repo)
        {
            _repo = repo;
        }

        // Returnează toate rețelele WiFi asociate unei persoane monitorizate
        public Task<IEnumerable<WifiNetwork>> GetByMonitoredIdAsync(Guid monitoredId)
            => _repo.GetByMonitoredIdAsync(monitoredId);

        // Returnează rețelele după numărul de serie al dispozitivului (ESP32 cere rețelele la boot)
        public Task<IEnumerable<WifiNetwork>> GetByDeviceSerialAsync(string serial)
            => _repo.GetByDeviceSerialAsync(serial);

        // Returnează o singură rețea după ID
        public Task<WifiNetwork?> GetByIdAsync(Guid id) => _repo.GetByIdAsync(id);

        // Adaugă o rețea WiFi nouă cu validări de business:
        //   - SSID obligatoriu, max 32 caractere (limita standardului WiFi 802.11)
        //   - Parolă max 64 caractere (limita WPA2)
        //   - Nu permite duplicate SSID pentru același dispozitiv
        //   - Maxim MaxNetworksPerDevice (3) rețele per dispozitiv
        // Returnează un tuplu (Success, ErrorKey, Network) pentru a putea trimite erori specifice în API
        public async Task<(bool Success, string? ErrorKey, WifiNetwork? Network)> AddAsync(Guid monitoredId, string ssid, string password)
        {
            ssid     = (ssid ?? string.Empty).Trim(); // Curățăm spații în jurul SSID-ului
            password ??= string.Empty;

            if (string.IsNullOrEmpty(ssid))
                return (false, "ssidRequired", null);
            if (ssid.Length > 32)                     // Limita standard WiFi 802.11 pentru SSID
                return (false, "ssidTooLong", null);
            if (password.Length > 64)                 // Limita WPA2 pentru parolă
                return (false, "passwordTooLong", null);

            // Verificăm dacă SSID-ul există deja (comparare exactă, case-sensitive — WiFi e case-sensitive)
            var existing = await _repo.GetByMonitoredIdAsync(monitoredId);
            if (existing.Any(w => string.Equals(w.Ssid, ssid, StringComparison.Ordinal)))
                return (false, "ssidDuplicate", null);

            // Verificăm că nu depășim limita de 3 rețele per dispozitiv
            var count = await _repo.CountByMonitoredIdAsync(monitoredId);
            if (count >= IWifiNetworkService.MaxNetworksPerDevice)
                return (false, "limitReached", null);

            var network = new WifiNetwork
            {
                Id          = Guid.NewGuid(),
                IdMonitored = monitoredId,
                Ssid        = ssid,
                Password    = password, // Stocată ca text simplu (fără criptare)
                CreatedAt   = DateTime.UtcNow
            };
            await _repo.AddAsync(network);
            return (true, null, network); // Succes: returnăm și entitatea creată
        }

        // Șterge o rețea WiFi după ID; returnează false dacă nu există
        public async Task<bool> DeleteAsync(Guid id)
        {
            var network = await _repo.GetByIdAsync(id);
            if (network == null) return false;
            await _repo.DeleteAsync(network);
            return true;
        }
    }
}
