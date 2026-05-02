using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace LifeAlertPlus.Infrastructure.Repositories
{
    public class MonitoredConditionRepository : IMonitoredConditionRepository
    {
        private readonly LifeAlertPlusDbContext _db;

        public MonitoredConditionRepository(LifeAlertPlusDbContext db)
        {
            _db = db;
        }

        public async Task<IEnumerable<MonitoredCondition>> GetByMonitoredIdAsync(Guid monitoredId) =>
            await _db.MonitoredConditions
                .Where(c => c.IdMonitored == monitoredId)
                .OrderBy(c => c.AddedAt)
                .ToListAsync();

        public async Task ReplaceAllAsync(Guid monitoredId, IEnumerable<string> conditionKeys)
        {
            var existing = await _db.MonitoredConditions
                .Where(c => c.IdMonitored == monitoredId)
                .ToListAsync();

            _db.MonitoredConditions.RemoveRange(existing);

            var now = DateTime.UtcNow;
            foreach (var key in conditionKeys.Distinct())
            {
                _db.MonitoredConditions.Add(new MonitoredCondition
                {
                    Id = Guid.NewGuid(),
                    IdMonitored = monitoredId,
                    ConditionKey = key,
                    AddedAt = now
                });
            }

            await _db.SaveChangesAsync();
        }
    }
}
