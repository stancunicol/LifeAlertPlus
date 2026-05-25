using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Application.IServices
{
    public interface IWifiNetworkService
    {
        public const int MaxNetworksPerDevice = 3;

        Task<IEnumerable<WifiNetwork>> GetByMonitoredIdAsync(Guid monitoredId);
        Task<IEnumerable<WifiNetwork>> GetByDeviceSerialAsync(string serial);
        Task<WifiNetwork?> GetByIdAsync(Guid id);
        Task<(bool Success, string? ErrorKey, WifiNetwork? Network)> AddAsync(Guid monitoredId, string ssid, string password);
        Task<bool> DeleteAsync(Guid id);
    }
}
