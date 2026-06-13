using System.Collections.Generic;
using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Domain.IRepositories
{
    public interface IUserMonitoredRepository
    {
        Task<IEnumerable<Monitored>> GetMonitoredPeopleWithDetailsByUserIdAsync(Guid userId);
        Task<IEnumerable<Monitored>> GetActiveMonitoredPeopleByUserIdAsync(Guid userId);
        Task<IEnumerable<Monitored>> GetArchivedMonitoredPeopleByUserIdAsync(Guid userId);
        Task<IEnumerable<UserMonitored>> GetAllUserMonitoredWithDetailsAsync();
        Task AddMonitoredPersonToUserAsync(Guid userId, Guid monitoredPersonId);
        Task<bool> UserOwnsMonitoredAsync(Guid userId, Guid monitoredId);
        Task<int> CountUsersForMonitoredAsync(Guid monitoredId);
        Task RemoveUserMonitoredLinkAsync(Guid userId, Guid monitoredId);
    }
}