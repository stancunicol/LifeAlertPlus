using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Application.IServices
{
    public interface IUserMonitoredService
    {
        Task<IEnumerable<Monitored>> GetMonitoredPeopleByUserIdAsync(Guid userId);
        Task AddMonitoredPersonToUserAsync(Guid userId, Guid monitoredPersonId);
    }
}