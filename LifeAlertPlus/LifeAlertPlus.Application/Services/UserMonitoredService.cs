using LifeAlertPlus.Shared.DTOs.Requests.Monitored;
using LifeAlertPlus.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeAlertPlus.Domain.IRepositories;

namespace LifeAlertPlus.Application.IServices
{
    public class UserMonitoredService : IUserMonitoredService
    {
        private readonly IUserMonitoredRepository _userMonitoredRepository;
        private readonly IMonitoredService _monitoredService;
        
        public UserMonitoredService(IUserMonitoredRepository userMonitoredRepository, IMonitoredService monitoredService)
        {
            _userMonitoredRepository = userMonitoredRepository;
            _monitoredService = monitoredService;
        }

        public async Task<IEnumerable<Monitored>> GetMonitoredPeopleByUserIdAsync(Guid userId)
        {
            var monitoredPeopleIds = await _userMonitoredRepository.GetMonitoredPeopleByUserIdAsync(userId);
            var monitoredPeople = new List<Monitored>();

            foreach (var monitoredId in monitoredPeopleIds)
            {
                var monitoredPerson = await _monitoredService.GetMonitoredPersonByIdAsync(monitoredId);
                if (monitoredPerson != null)
                {
                    monitoredPeople.Add(monitoredPerson);
                }
            }

            return monitoredPeople;
        }

        public async Task AddMonitoredPersonToUserAsync(Guid userId, Guid monitoredPersonId)
        {
            await _userMonitoredRepository.AddMonitoredPersonToUserAsync(userId, monitoredPersonId);
        }
    }
}