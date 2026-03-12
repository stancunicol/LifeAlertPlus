using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Domain.IRepositories
{
    public interface IUserMonitoredRepository
    {
        Task<IEnumerable<Monitored>> GetMonitoredPeopleWithDetailsByUserIdAsync(Guid userId);
        Task AddMonitoredPersonToUserAsync(Guid userId, Guid monitoredPersonId);
    }
}