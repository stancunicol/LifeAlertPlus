using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Shared.DTOs.Responses.Measurement;

namespace LifeAlertPlus.Application.IServices
{
    public interface IMeasurementService
    {
        Task AddMeasurementAsync(Measurement measurement);
        Task<IEnumerable<MeasurementResponseDTO>?> GetMeasurementsByMonitoredIdAsync(Guid idMonitored, int pageNumber, int pageSize);
        Task<MeasurementResponseDTO?> GetMeasurementByIdAsync(Guid id);
    }
}