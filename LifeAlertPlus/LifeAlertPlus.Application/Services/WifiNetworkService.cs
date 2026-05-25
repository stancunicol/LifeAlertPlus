using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;

namespace LifeAlertPlus.Application.Services
{
    public class WifiNetworkService : IWifiNetworkService
    {
        private readonly IWifiNetworkRepository _repo;

        public WifiNetworkService(IWifiNetworkRepository repo)
        {
            _repo = repo;
        }

        public Task<IEnumerable<WifiNetwork>> GetByMonitoredIdAsync(Guid monitoredId)
            => _repo.GetByMonitoredIdAsync(monitoredId);

        public Task<IEnumerable<WifiNetwork>> GetByDeviceSerialAsync(string serial)
            => _repo.GetByDeviceSerialAsync(serial);

        public Task<WifiNetwork?> GetByIdAsync(Guid id) => _repo.GetByIdAsync(id);

        public async Task<(bool Success, string? ErrorKey, WifiNetwork? Network)> AddAsync(Guid monitoredId, string ssid, string password)
        {
            ssid = (ssid ?? string.Empty).Trim();
            password ??= string.Empty;

            if (string.IsNullOrEmpty(ssid))
                return (false, "ssidRequired", null);
            if (ssid.Length > 32)
                return (false, "ssidTooLong", null);
            if (password.Length > 64)
                return (false, "passwordTooLong", null);

            var existing = await _repo.GetByMonitoredIdAsync(monitoredId);
            if (existing.Any(w => string.Equals(w.Ssid, ssid, StringComparison.Ordinal)))
                return (false, "ssidDuplicate", null);

            var count = await _repo.CountByMonitoredIdAsync(monitoredId);
            if (count >= IWifiNetworkService.MaxNetworksPerDevice)
                return (false, "limitReached", null);

            var network = new WifiNetwork
            {
                Id = Guid.NewGuid(),
                IdMonitored = monitoredId,
                Ssid = ssid,
                Password = password,
                CreatedAt = DateTime.UtcNow
            };
            await _repo.AddAsync(network);
            return (true, null, network);
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var network = await _repo.GetByIdAsync(id);
            if (network == null) return false;
            await _repo.DeleteAsync(network);
            return true;
        }
    }
}
