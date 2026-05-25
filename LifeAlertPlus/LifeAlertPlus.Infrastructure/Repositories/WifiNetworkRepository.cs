using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace LifeAlertPlus.Infrastructure.Repositories
{
    public class WifiNetworkRepository : IWifiNetworkRepository
    {
        private readonly LifeAlertPlusDbContext _db;

        public WifiNetworkRepository(LifeAlertPlusDbContext db)
        {
            _db = db;
        }

        public async Task<IEnumerable<WifiNetwork>> GetByMonitoredIdAsync(Guid monitoredId) =>
            await _db.WifiNetworks
                .Where(w => w.IdMonitored == monitoredId)
                .OrderBy(w => w.CreatedAt)
                .ToListAsync();

        public async Task<IEnumerable<WifiNetwork>> GetByDeviceSerialAsync(string serial) =>
            await _db.WifiNetworks
                .Where(w => w.Monitored.DeviceSerialNumber == serial)
                .OrderBy(w => w.CreatedAt)
                .ToListAsync();

        public async Task<WifiNetwork?> GetByIdAsync(Guid id) =>
            await _db.WifiNetworks.FirstOrDefaultAsync(w => w.Id == id);

        public async Task<int> CountByMonitoredIdAsync(Guid monitoredId) =>
            await _db.WifiNetworks.CountAsync(w => w.IdMonitored == monitoredId);

        public async Task AddAsync(WifiNetwork network)
        {
            _db.WifiNetworks.Add(network);
            await _db.SaveChangesAsync();
        }

        public async Task DeleteAsync(WifiNetwork network)
        {
            _db.WifiNetworks.Remove(network);
            await _db.SaveChangesAsync();
        }
    }
}
