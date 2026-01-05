using LifeAlertPlus.Domain.Entities;
using System;

namespace LifeAlertPlus.Domain.IRepositories
{
    public interface IUserMonitoredRepository
    {
        Task<IEnumerable<Guid>> GetMonitoredPeopleByUserIdAsync(Guid userId);
        Task AddMonitoredPersonToUserAsync(Guid userId, Guid monitoredPersonId);
    }
}