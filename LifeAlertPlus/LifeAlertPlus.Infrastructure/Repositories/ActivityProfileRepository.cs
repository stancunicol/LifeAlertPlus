using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace LifeAlertPlus.Infrastructure.Repositories
{
    // Implementare EF Core a IActivityProfileRepository — profilul comportamental orar (24 înregistrări/pacient)
    public class ActivityProfileRepository : IActivityProfileRepository
    {
        private readonly LifeAlertPlusDbContext _db;

        public ActivityProfileRepository(LifeAlertPlusDbContext db)
        {
            _db = db;
        }

        // SELECT toate cele 24 ore de profil, ordonate cronologic (0-23)
        public async Task<IEnumerable<ActivityProfile>> GetByMonitoredIdAsync(Guid monitoredId) =>
            await _db.ActivityProfiles
                .Where(p => p.IdMonitored == monitoredId)
                .OrderBy(p => p.HourOfDay)
                .ToListAsync();

        // Upsert manual: căutăm înregistrarea pentru (pacient, oră) — dacă există, actualizăm câmpurile;
        // altfel inserăm una nouă. Folosit de rebuild-ul zilnic al profilului (fereastră 7 zile).
        public async Task UpsertAsync(ActivityProfile profile)
        {
            var existing = await _db.ActivityProfiles
                .FirstOrDefaultAsync(p => p.IdMonitored == profile.IdMonitored && p.HourOfDay == profile.HourOfDay);

            if (existing == null)
            {
                _db.ActivityProfiles.Add(profile);
            }
            else
            {
                existing.AveragePulse = profile.AveragePulse;
                existing.MovementRate = profile.MovementRate;
                existing.SleepProbability = profile.SleepProbability;
                existing.DataPoints = profile.DataPoints;
                existing.LastUpdated = profile.LastUpdated;
            }

            await _db.SaveChangesAsync();
        }

        // DELETE bulk — șterge tot profilul unei persoane (ex: la ștergerea contului/pacientului)
        public async Task DeleteByMonitoredIdAsync(Guid monitoredId)
        {
            await _db.ActivityProfiles
                .Where(p => p.IdMonitored == monitoredId)
                .ExecuteDeleteAsync();
        }
    }
}
