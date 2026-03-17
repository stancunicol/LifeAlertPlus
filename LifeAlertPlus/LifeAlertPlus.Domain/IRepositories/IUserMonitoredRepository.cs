using System.Collections.Generic;
using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Domain.IRepositories
{
    public interface IUserMonitoredRepository
    {
        Task<IEnumerable<Monitored>> GetMonitoredPeopleWithDetailsByUserIdAsync(Guid userId);
        Task<IEnumerable<UserMonitored>> GetAllUserMonitoredWithDetailsAsync();
        Task AddMonitoredPersonToUserAsync(Guid userId, Guid monitoredPersonId);
    }
}