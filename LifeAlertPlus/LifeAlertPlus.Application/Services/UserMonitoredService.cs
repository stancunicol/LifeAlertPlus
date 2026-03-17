using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Application.IServices;

namespace LifeAlertPlus.Application.Services
{
    public class UserMonitoredService : IUserMonitoredService
    {
        private readonly IUserMonitoredRepository _userMonitoredRepository;
        
        public UserMonitoredService(IUserMonitoredRepository userMonitoredRepository)
        {
            _userMonitoredRepository = userMonitoredRepository;
        }

        public async Task<IEnumerable<Monitored>> GetMonitoredPeopleByUserIdAsync(Guid userId)
        {
            return await _userMonitoredRepository.GetMonitoredPeopleWithDetailsByUserIdAsync(userId);
        }

        public async Task<IEnumerable<UserMonitored>> GetAllUserMonitoredWithDetailsAsync()
        {
            return await _userMonitoredRepository.GetAllUserMonitoredWithDetailsAsync();
        }

        public async Task AddMonitoredPersonToUserAsync(Guid userId, Guid monitoredPersonId)
        {
            await _userMonitoredRepository.AddMonitoredPersonToUserAsync(userId, monitoredPersonId);
        }
    }
}