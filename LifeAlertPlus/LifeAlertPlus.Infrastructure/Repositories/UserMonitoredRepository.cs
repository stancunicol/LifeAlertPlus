using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeAlertPlus.Infrastructure.Context;

namespace LifeAlertPlus.Infrastructure.Repositories
{
    public class UserMonitoredRepository : IUserMonitoredRepository
    {
        private readonly LifeAlertPlusDbContext _context;

        public UserMonitoredRepository(LifeAlertPlusDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Guid>> GetMonitoredPeopleByUserIdAsync(Guid userId)
        {
            var monitoredPeople = await _context.UserMonitoreds
                .Include(um => um.Monitored)
                .Where(um => um.IdUser == userId)
                .Select(um => um.IdMonitored)
                .ToListAsync();

            return monitoredPeople;
        }

        public async Task AddMonitoredPersonToUserAsync(Guid userId, Guid monitoredPersonId)
        {
            var userMonitored = new UserMonitored
            {
                IdUser = userId,
                IdMonitored = monitoredPersonId
            };

            _context.UserMonitoreds.Add(userMonitored);
            await _context.SaveChangesAsync();
        }
    }
}