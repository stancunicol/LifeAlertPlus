using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Shared.DTOs.Requests.Monitored;

namespace LifeAlertPlus.Application.IServices
{
    public interface IMonitoredService
    {
        Task<Monitored> AddMonitoredPersonAsync(MonitorCreateRequestDTO monitoredPersonDto);
        Task<Monitored?> GetMonitoredPersonByDeviceSerialNumberAsync(string deviceSerialNumber);
        Task<Monitored?> GetMonitoredPersonByIdAsync(Guid id);
        Task<IEnumerable<Monitored>> GetAllMonitoredPeopleAsync();
        Task UpdateMonitoredPersonAsync(Monitored monitoredPerson);
        Task DeleteMonitoredPersonAsync(Guid id);
        Task<bool> ArchiveMonitoredPersonAsync(Guid id);
        Task<bool> RestoreMonitoredPersonAsync(Guid id);
        Task<bool> SoftDeleteMonitoredPersonAsync(Guid id);
        Task<bool> ReactivateMonitoredPersonAsync(Guid id);
    }
}