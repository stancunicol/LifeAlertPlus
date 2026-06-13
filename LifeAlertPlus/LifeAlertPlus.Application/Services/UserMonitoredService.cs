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

        public async Task<IEnumerable<Monitored>> GetActiveMonitoredPeopleByUserIdAsync(Guid userId)
        {
            return await _userMonitoredRepository.GetActiveMonitoredPeopleByUserIdAsync(userId);
        }

        public async Task<IEnumerable<Monitored>> GetArchivedMonitoredPeopleByUserIdAsync(Guid userId)
        {
            return await _userMonitoredRepository.GetArchivedMonitoredPeopleByUserIdAsync(userId);
        }

        public async Task<IEnumerable<UserMonitored>> GetAllUserMonitoredWithDetailsAsync()
        {
            return await _userMonitoredRepository.GetAllUserMonitoredWithDetailsAsync();
        }

        public async Task AddMonitoredPersonToUserAsync(Guid userId, Guid monitoredPersonId)
        {
            await _userMonitoredRepository.AddMonitoredPersonToUserAsync(userId, monitoredPersonId);
        }

        public async Task<bool> UserOwnsMonitoredAsync(Guid userId, Guid monitoredId) =>
            await _userMonitoredRepository.UserOwnsMonitoredAsync(userId, monitoredId);

        public async Task<int> CountUsersForMonitoredAsync(Guid monitoredId) =>
            await _userMonitoredRepository.CountUsersForMonitoredAsync(monitoredId);

        public async Task RemoveUserMonitoredLinkAsync(Guid userId, Guid monitoredId) =>
            await _userMonitoredRepository.RemoveUserMonitoredLinkAsync(userId, monitoredId);
    }
}