using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace LifeAlertPlus.Infrastructure.Repositories
{
    // Implementare EF Core a IMonitoredConditionRepository — afecțiunile diagnosticate ale pacientului
    public class MonitoredConditionRepository : IMonitoredConditionRepository
    {
        private readonly LifeAlertPlusDbContext _db;

        public MonitoredConditionRepository(LifeAlertPlusDbContext db)
        {
            _db = db;
        }

        // SELECT toate afecțiunile unei persoane, ordonate cronologic după data adăugării
        public async Task<IEnumerable<MonitoredCondition>> GetByMonitoredIdAsync(Guid monitoredId) =>
            await _db.MonitoredConditions
                .Where(c => c.IdMonitored == monitoredId)
                .OrderBy(c => c.AddedAt)
                .ToListAsync();

        // Înlocuiește complet lista de afecțiuni: șterge tot ce exista, inserează lista nouă
        // Mai simplu decât un diff (adăugare/ștergere individuală) — frecvența operației e mică (editare profil)
        public async Task ReplaceAllAsync(Guid monitoredId, IEnumerable<string> conditionKeys)
        {
            var existing = await _db.MonitoredConditions
                .Where(c => c.IdMonitored == monitoredId)
                .ToListAsync();

            _db.MonitoredConditions.RemoveRange(existing);

            var now = DateTime.UtcNow;
            foreach (var key in conditionKeys.Distinct()) // Distinct: evită duplicate dacă vin din UI
            {
                _db.MonitoredConditions.Add(new MonitoredCondition
                {
                    Id = Guid.NewGuid(),
                    IdMonitored = monitoredId,
                    ConditionKey = key,
                    AddedAt = now
                });
            }

            await _db.SaveChangesAsync(); // DELETE + INSERT-urile se salvează într-o singură tranzacție implicită
        }
    }
}
