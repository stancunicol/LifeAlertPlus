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

            if (monitoredIds.Count > 0)
            {
                // Exclude monitored people shared with other users — one batched query instead of N
                var sharedIds = await _dbContext.UserMonitoreds
                    .Where(um => um.IdUser != id && monitoredIds.Contains(um.IdMonitored))
                    .Select(um => um.IdMonitored)
                    .Distinct()
                    .ToListAsync();

                var exclusiveIds = monitoredIds.Except(sharedIds).ToList();

                if (exclusiveIds.Count > 0)
                {
                    await _dbContext.Measurements.Where(m => exclusiveIds.Contains(m.IdMonitored)).ExecuteDeleteAsync();
                    await _dbContext.Notifications.Where(n => exclusiveIds.Contains(n.IdMonitored)).ExecuteDeleteAsync();
                    await _dbContext.DailyHistories.Where(d => exclusiveIds.Contains(d.IdMonitored)).ExecuteDeleteAsync();
                    await _dbContext.WeeklyHistories.Where(w => exclusiveIds.Contains(w.IdMonitored)).ExecuteDeleteAsync();
                    await _dbContext.Monitoreds.Where(m => exclusiveIds.Contains(m.Id)).ExecuteDeleteAsync();
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
