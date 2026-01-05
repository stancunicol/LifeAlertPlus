using LifeAlertPlus.Shared.DTOs.Requests.Monitored;
using LifeAlertPlus.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LifeAlertPlus.Application.IServices
{
    public interface IUserMonitoredService
    {
        Task<IEnumerable<Monitored>> GetMonitoredPeopleByUserIdAsync(Guid userId);
        Task AddMonitoredPersonToUserAsync(Guid userId, Guid monitoredPersonId);
    }
}