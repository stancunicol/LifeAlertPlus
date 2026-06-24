using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;
using Microsoft.EntityFrameworkCore;
using LifeAlertPlus.Infrastructure.Context;

namespace LifeAlertPlus.Infrastructure.Repositories
{
    // Implementare EF Core a IMonitoredRepository — pacienții monitorizați (entitatea centrală a sistemului)
    public class MonitoredRepository : IMonitoredRepository
    {
        private readonly LifeAlertPlusDbContext _context;

        public MonitoredRepository(LifeAlertPlusDbContext context)
        {
            _context = context;
        }

        // INSERT pacient nou
        public async Task<Monitored> AddMonitoredPersonAsync(Monitored monitoredPerson)
        {
            _context.Monitoreds.Add(monitoredPerson);
            await _context.SaveChangesAsync();
            return monitoredPerson;
        }

        // DELETE permanent (hard-delete) — folosit doar după arhivare/soft-delete prealabilă
        public async Task DeleteMonitoredPersonAsync(Guid id)
        {
            var monitoredPerson = await _context.Monitoreds.FindAsync(id);
            if (monitoredPerson != null)
            {
                _context.Monitoreds.Remove(monitoredPerson);
                await _context.SaveChangesAsync();
            }
        }

        // SELECT toți pacienții din sistem (Admin)
        public async Task<IEnumerable<Monitored>> GetAllMonitoredPeopleAsync()
        {
            return await _context.Monitoreds.ToListAsync();
        }

        // SELECT după seria dispozitivului ESP32 — folosit la ingestia datelor de la senzor
        public async Task<Monitored?> GetMonitoredPersonByDeviceSerialNumberAsync(string deviceSerialNumber)
        {
            return await _context.Monitoreds
                .FirstOrDefaultAsync(m => m.DeviceSerialNumber == deviceSerialNumber);
        }

        // SELECT după ID
        public async Task<Monitored?> GetMonitoredPersonByIdAsync(Guid id)
        {
            return await _context.Monitoreds.FindAsync(id);
        }

        // UPDATE pacient existent
        public async Task UpdateMonitoredPersonAsync(Monitored monitoredPerson)
        {
            _context.Monitoreds.Update(monitoredPerson);
            await _context.SaveChangesAsync();
        }

        // Arhivare: oprește monitorizarea activă, marchează IsArchived + ArchivedAt; datele rămân în DB
        public async Task<bool> ArchiveMonitoredPersonAsync(Guid id, DateTime archivedAt)
        {
            var monitored = await _context.Monitoreds.FindAsync(id);
            if (monitored == null) return false;
            monitored.IsArchived = true;
            monitored.ArchivedAt = archivedAt;
            monitored.UpdatedAt = archivedAt;
            await _context.SaveChangesAsync();
            return true;
        }

        // Dezarhivare: curăță flagurile de arhivare — pacientul revine în monitorizare activă
        public async Task<bool> RestoreMonitoredPersonAsync(Guid id)
        {
            var monitored = await _context.Monitoreds.FindAsync(id);
            if (monitored == null) return false;
            monitored.IsArchived = false;
            monitored.ArchivedAt = null;
            monitored.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }

        // Soft-delete: marchează DeletedAt — pacientul intră în perioada de grație (7 zile) înainte de ștergere permanentă
        public async Task<bool> SoftDeleteMonitoredPersonAsync(Guid id, DateTime deletedAt)
        {
            var monitored = await _context.Monitoreds.FindAsync(id);
            if (monitored == null) return false;
            monitored.DeletedAt = deletedAt;
            monitored.UpdatedAt = deletedAt;
            await _context.SaveChangesAsync();
            return true;
        }

        // Reactivare (Admin): anulează soft-delete-ul înainte de expirarea perioadei de grație
        public async Task<bool> ReactivateMonitoredPersonAsync(Guid id)
        {
            var monitored = await _context.Monitoreds.FindAsync(id);
            if (monitored == null) return false;
            monitored.DeletedAt = null;
            monitored.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }
    }
}