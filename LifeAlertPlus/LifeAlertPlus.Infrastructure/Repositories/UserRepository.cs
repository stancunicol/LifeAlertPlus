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

        public async Task<User?> GetUserByPhoneNumberAsync(string phoneNumber)
        {
            return await _dbContext.Users
                .FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber);
        }

        public async Task<User?> GetUserByIdAsync(Guid id)
        {
            return await _dbContext.Users
                .FirstOrDefaultAsync(u => u.Id == id);
        }

        public async Task<IEnumerable<User>> GetAllUsersAsync()
        {
            return await _dbContext.Users
                .Include(u => u.Role)
                .ToListAsync();
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

            var userMonitoreds = await _dbContext.UserMonitoreds.Where(um => um.IdUser == id).ToListAsync();
            var monitoredIds = userMonitoreds.Select(um => um.IdMonitored).Distinct().ToList();
            _dbContext.UserMonitoreds.RemoveRange(userMonitoreds);

            foreach (var monitoredId in monitoredIds)
            {
                // Only delete the monitored person and their data if no other user also monitors them
                var hasOtherMonitors = await _dbContext.UserMonitoreds
                    .AnyAsync(um => um.IdMonitored == monitoredId && um.IdUser != id);

                if (hasOtherMonitors)
                    continue;

                var measurements = await _dbContext.Measurements.Where(m => m.IdMonitored == monitoredId).ToListAsync();
                _dbContext.Measurements.RemoveRange(measurements);

                var notifications = await _dbContext.Notifications.Where(n => n.IdMonitored == monitoredId).ToListAsync();
                _dbContext.Notifications.RemoveRange(notifications);

                var dailyHistories = await _dbContext.DailyHistories.Where(d => d.IdMonitored == monitoredId).ToListAsync();
                _dbContext.DailyHistories.RemoveRange(dailyHistories);

                var weeklyHistories = await _dbContext.WeeklyHistories.Where(w => w.IdMonitored == monitoredId).ToListAsync();
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

        public async Task<User?> GetUserByEmailChangeTokenAsync(string token)
        {
            return await _dbContext.Users.FirstOrDefaultAsync(u => u.EmailChangeToken == token);
        }

        public async Task<User?> GetUserByEmailConfirmationTokenAsync(string token)
        {
            return await _dbContext.Users.FirstOrDefaultAsync(u => u.EmailConfirmationToken == token);
        }

        public async Task<User?> GetUserByPasswordResetTokenAsync(string token)
        {
            return await _dbContext.Users.FirstOrDefaultAsync(u => u.PasswordResetToken == token);
        }

        public async Task<User?> GetUserByEmailChangeCancelTokenAsync(string token)
        {
            return await _dbContext.Users.FirstOrDefaultAsync(u => u.EmailChangeCancelToken == token);
        }
    }
}
