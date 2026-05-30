using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Domain.IRepositories
{
    public interface IMonitoredRepository
    {
        Task<Monitored> AddMonitoredPersonAsync(Monitored monitoredPerson);
        Task<Monitored?> GetMonitoredPersonByDeviceSerialNumberAsync(string deviceSerialNumber);
        Task<Monitored?> GetMonitoredPersonByIdAsync(Guid id);
        Task<IEnumerable<Monitored>> GetAllMonitoredPeopleAsync();
        Task UpdateMonitoredPersonAsync(Monitored monitoredPerson);
        Task DeleteMonitoredPersonAsync(Guid id);
        Task<bool> ArchiveMonitoredPersonAsync(Guid id, DateTime archivedAt);
        Task<bool> RestoreMonitoredPersonAsync(Guid id);
    }
}