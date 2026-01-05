using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace LifeAlertPlus.Infrastructure.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly LifeAlertPlusDbContext _dbContext;
        public UserRepository(LifeAlertPlusDbContext dbContext)
        {
            _dbContext = dbContext;
        }
        public async Task<User?> GetUserByEmailAsync(string email)
        {
            return await _dbContext.Users
                .FirstOrDefaultAsync(u => u.Email == email);
        }

        public async Task<User?> GetUserByIdAsync(Guid id)
        {
            return await _dbContext.Users
                .FirstOrDefaultAsync(u => u.Id == id);
        }

        public async Task<IEnumerable<User>> GetAllUsersAsync()
        {
            return await _dbContext.Users.ToListAsync();
        }

        public async Task<bool> CreateUserAsync(User user)
        {
            _dbContext.Users.Add(user);
            var result = await _dbContext.SaveChangesAsync();
            return result > 0;
        }

        public async Task<bool> UpdateUserAsync(User user)
        {
            _dbContext.Users.Update(user);
            var result = await _dbContext.SaveChangesAsync();
            return result > 0;
        }

        public async Task<bool> DeleteUserAsync(Guid id)
        {
            var user = await _dbContext.Users.FindAsync(id);
            if (user == null)
            {
                return false;
            }

            var userMonitoreds = _dbContext.UserMonitoreds.Where(um => um.IdUser == id).ToList();
            var monitoredIds = userMonitoreds.Select(um => um.IdMonitored).Distinct().ToList();
            _dbContext.UserMonitoreds.RemoveRange(userMonitoreds);

            foreach (var monitoredId in monitoredIds)
            {
                var measurements = _dbContext.Measurements.Where(m => m.IdMonitored == monitoredId);
                _dbContext.Measurements.RemoveRange(measurements);

                var notifications = _dbContext.Notifications.Where(n => n.IdMonitored == monitoredId);
                _dbContext.Notifications.RemoveRange(notifications);

                var dailyHistories = _dbContext.DailyHistories.Where(d => d.IdMonitored == monitoredId);
                _dbContext.DailyHistories.RemoveRange(dailyHistories);

                var weeklyHistories = _dbContext.WeeklyHistories.Where(w => w.IdMonitored == monitoredId);
                _dbContext.WeeklyHistories.RemoveRange(weeklyHistories);

                var monitored = await _dbContext.Monitoreds.FindAsync(monitoredId);
                if (monitored != null)
                {
                    _dbContext.Monitoreds.Remove(monitored);
                }
            }

            _dbContext.Users.Remove(user);
            var result = await _dbContext.SaveChangesAsync();
            return result > 0;
        }
    }
}
