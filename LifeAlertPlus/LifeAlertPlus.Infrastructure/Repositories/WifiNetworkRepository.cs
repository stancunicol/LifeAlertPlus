using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace LifeAlertPlus.Infrastructure.Repositories
{
    // Implementare EF Core a IWifiNetworkRepository — rețelele WiFi salvate pentru ESP32
    // NOTĂ: parola este stocată ca text simplu (nu există criptare configurată)
    public class WifiNetworkRepository : IWifiNetworkRepository
    {
        private readonly LifeAlertPlusDbContext _db;

        public WifiNetworkRepository(LifeAlertPlusDbContext db)
        {
            _db = db;
        }

        // SELECT toate rețelele unei persoane monitorizate, ordonate cronologic
        public async Task<IEnumerable<WifiNetwork>> GetByMonitoredIdAsync(Guid monitoredId) =>
            await _db.WifiNetworks
                .Where(w => w.IdMonitored == monitoredId)
                .OrderBy(w => w.CreatedAt)
                .ToListAsync();

        // SELECT prin JOIN cu Monitored după seria dispozitivului — ESP32 cere lista de rețele la boot
        public async Task<IEnumerable<WifiNetwork>> GetByDeviceSerialAsync(string serial) =>
            await _db.WifiNetworks
                .Where(w => w.Monitored.DeviceSerialNumber == serial)
                .OrderBy(w => w.CreatedAt)
                .ToListAsync();

        // SELECT după ID
        public async Task<WifiNetwork?> GetByIdAsync(Guid id) =>
            await _db.WifiNetworks.FirstOrDefaultAsync(w => w.Id == id);

        // COUNT rețele existente — folosit de serviciu pentru a impune limita MaxNetworksPerDevice (3)
        public async Task<int> CountByMonitoredIdAsync(Guid monitoredId) =>
            await _db.WifiNetworks.CountAsync(w => w.IdMonitored == monitoredId);

        // INSERT + commit
        public async Task AddAsync(WifiNetwork network)
        {
            _db.WifiNetworks.Add(network);
            await _db.SaveChangesAsync();
        }

        // DELETE + commit
        public async Task DeleteAsync(WifiNetwork network)
        {
            _db.WifiNetworks.Remove(network);
            await _db.SaveChangesAsync();
        }
    }
}
