using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Domain.IRepositories
{
    public interface IMeasurementRepository
    {
        Task AddMeasurementAsync(Measurement measurement);
        Task<IEnumerable<Measurement>> GetMeasurementsByMonitoredIdAsync(Guid idMonitored, int pageNumber, int pageSize);
        Task<Measurement?> GetMeasurementByIdAsync(Guid id);
    }
}