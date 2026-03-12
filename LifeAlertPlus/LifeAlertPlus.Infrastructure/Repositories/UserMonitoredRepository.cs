using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;
using Microsoft.EntityFrameworkCore;
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

        public async Task<IEnumerable<Monitored>> GetMonitoredPeopleWithDetailsByUserIdAsync(Guid userId)
        {
            return await _context.UserMonitoreds
                .Where(um => um.IdUser == userId)
                .Select(um => um.Monitored)
                .ToListAsync();
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