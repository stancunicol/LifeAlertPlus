using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace LifeAlertPlus.Infrastructure.Repositories
{
    // Implementare EF Core a IMeasurementRepository — date trimise de ESP32 (puls, SpO2, temperatură, GPS, mișcare)
    public class MeasurementRepository : IMeasurementRepository
    {
        private readonly LifeAlertPlusDbContext _dbContext;
        public MeasurementRepository(LifeAlertPlusDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        // INSERT măsurătoare nouă + commit
        public async Task AddMeasurementAsync(Measurement measurement)
        {
            _dbContext.Measurements.Add(measurement);
            await _dbContext.SaveChangesAsync();
        }

        // SELECT paginat, ordonat descrescător (cele mai recente măsurători primele)
        public async Task<IEnumerable<Measurement>> GetMeasurementsByMonitoredIdAsync(Guid idMonitored, int pageNumber, int pageSize)
        {
            return await _dbContext.Measurements
                .Where(m => m.IdMonitored == idMonitored)
                .OrderByDescending(m => m.CreatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        // SELECT o singură măsurătoare după ID
        public async Task<Measurement?> GetMeasurementByIdAsync(Guid id)
        {
            return await _dbContext.Measurements
                .FirstOrDefaultAsync(m => m.Id == id);
        }

        // COUNT măsurători înregistrate azi (UTC) — statistică pentru dashboard-ul Admin
        public async Task<int> GetTodayMeasurementsCountAsync()
        {
            var today = DateTime.UtcNow.Date;
            var tomorrow = today.AddDays(1);

            return await _dbContext.Measurements
                .Where(m => m.CreatedAt >= today && m.CreatedAt < tomorrow)
                .CountAsync();
        }

        // DELETE bulk pentru politica de retenție — șterge măsurătorile mai vechi de cutoffDate
        // pentru lista de pacienți dată (ExecuteDeleteAsync = DELETE direct în DB, fără încărcare în memorie)
        public async Task<int> DeleteMeasurementsOlderThanAsync(IEnumerable<Guid> monitoredIds, DateTime cutoffDate)
        {
            var ids = monitoredIds.ToList();
            return await _dbContext.Measurements
                .Where(m => ids.Contains(m.IdMonitored) && m.CreatedAt < cutoffDate)
                .ExecuteDeleteAsync();
        }
    }
}