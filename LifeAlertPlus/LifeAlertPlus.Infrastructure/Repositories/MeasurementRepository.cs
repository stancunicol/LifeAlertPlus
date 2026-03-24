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
    }
}