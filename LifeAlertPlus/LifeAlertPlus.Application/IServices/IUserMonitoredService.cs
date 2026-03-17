using System;
using System.Collections.Generic;
using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Application.IServices
{
    public interface IUserMonitoredService
    {
        Task<IEnumerable<Monitored>> GetMonitoredPeopleByUserIdAsync(Guid userId);
        Task<IEnumerable<UserMonitored>> GetAllUserMonitoredWithDetailsAsync();
        Task AddMonitoredPersonToUserAsync(Guid userId, Guid monitoredPersonId);
    }
}