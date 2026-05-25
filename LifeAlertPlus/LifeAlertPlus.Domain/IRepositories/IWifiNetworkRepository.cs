using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Domain.IRepositories
{
    public interface IWifiNetworkRepository
    {
        Task<IEnumerable<WifiNetwork>> GetByMonitoredIdAsync(Guid monitoredId);
        Task<IEnumerable<WifiNetwork>> GetByDeviceSerialAsync(string serial);
        Task<WifiNetwork?> GetByIdAsync(Guid id);
        Task<int> CountByMonitoredIdAsync(Guid monitoredId);
        Task AddAsync(WifiNetwork network);
        Task DeleteAsync(WifiNetwork network);
    }
}
