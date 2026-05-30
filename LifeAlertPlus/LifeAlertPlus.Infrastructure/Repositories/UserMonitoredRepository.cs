using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

        public async Task<IEnumerable<Monitored>> GetActiveMonitoredPeopleByUserIdAsync(Guid userId)
        {
            return await _context.UserMonitoreds
                .Where(um => um.IdUser == userId)
                .Select(um => um.Monitored)
                .Where(m => !m.IsArchived && m.DeletedAt == null)
                .ToListAsync();
        }

        public async Task<IEnumerable<Monitored>> GetArchivedMonitoredPeopleByUserIdAsync(Guid userId)
        {
            return await _context.UserMonitoreds
                .Where(um => um.IdUser == userId)
                .Select(um => um.Monitored)
                .Where(m => m.IsArchived && m.DeletedAt == null)
                .ToListAsync();
        }

        public async Task<IEnumerable<UserMonitored>> GetAllUserMonitoredWithDetailsAsync()
        {
            return await _context.UserMonitoreds
                .Include(um => um.User)
                .Include(um => um.Monitored)
                .ToListAsync();
        }

        public async Task AddMonitoredPersonToUserAsync(Guid userId, Guid monitoredPersonId)
        {
            // Check if relationship already exists
            var exists = await _context.UserMonitoreds
                .AnyAsync(um => um.IdUser == userId && um.IdMonitored == monitoredPersonId);
            
            if (exists)
            {
                // Relationship already exists, skip adding
                return;
            }

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