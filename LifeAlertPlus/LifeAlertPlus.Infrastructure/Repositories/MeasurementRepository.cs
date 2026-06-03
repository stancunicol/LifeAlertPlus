using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace LifeAlertPlus.Infrastructure.Repositories
{
    public class MeasurementRepository : IMeasurementRepository
    {
        private readonly LifeAlertPlusDbContext _dbContext;
        public MeasurementRepository(LifeAlertPlusDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task AddMeasurementAsync(Measurement measurement)
        {
            _dbContext.Measurements.Add(measurement);
            await _dbContext.SaveChangesAsync();
        }

        public async Task<IEnumerable<Measurement>> GetMeasurementsByMonitoredIdAsync(Guid idMonitored, int pageNumber, int pageSize)
        {
            return await _dbContext.Measurements
                .Where(m => m.IdMonitored == idMonitored)
                .OrderByDescending(m => m.CreatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<Measurement?> GetMeasurementByIdAsync(Guid id)
        {
            return await _dbContext.Measurements
                .FirstOrDefaultAsync(m => m.Id == id);
        }

        public async Task<int> GetTodayMeasurementsCountAsync()
        {
            var today = DateTime.UtcNow.Date;
            var tomorrow = today.AddDays(1);
            
            return await _dbContext.Measurements
                .Where(m => m.CreatedAt >= today && m.CreatedAt < tomorrow)
                .CountAsync();
        }

        public async Task<int> DeleteMeasurementsOlderThanAsync(IEnumerable<Guid> monitoredIds, DateTime cutoffDate)
        {
            var ids = monitoredIds.ToList();
            return await _dbContext.Measurements
                .Where(m => ids.Contains(m.IdMonitored) && m.CreatedAt < cutoffDate)
                .ExecuteDeleteAsync();
        }
    }
}