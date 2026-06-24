using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace LifeAlertPlus.Infrastructure.Repositories
{
    // Implementare EF Core a IUserRepository — conturile de utilizatori (îngrijitori, admini)
    public class UserRepository : IUserRepository
    {
        private readonly LifeAlertPlusDbContext _dbContext;
        public UserRepository(LifeAlertPlusDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        // SELECT după email — folosit la autentificare și verificare duplicate
        public async Task<User?> GetUserByEmailAsync(string email)
        {
            return await _dbContext.Users
                .FirstOrDefaultAsync(u => u.Email == email);
        }

        // SELECT după telefon — verificare duplicate la înregistrare
        public async Task<User?> GetUserByPhoneNumberAsync(string phoneNumber)
        {
            return await _dbContext.Users
                .FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber);
        }

        // SELECT după ID — cel mai folosit, vine din claims JWT
        public async Task<User?> GetUserByIdAsync(Guid id)
        {
            return await _dbContext.Users
                .FirstOrDefaultAsync(u => u.Id == id);
        }

        // SELECT toți utilizatorii + rolul lor (eager loading) — folosit de Admin
        public async Task<IEnumerable<User>> GetAllUsersAsync()
        {
            return await _dbContext.Users
                .Include(u => u.Role)
                .ToListAsync();
        }

        // INSERT cont nou; returnează true doar dacă SaveChanges a afectat cel puțin un rând
        public async Task<bool> CreateUserAsync(User user)
        {
            _dbContext.Users.Add(user);
            var result = await _dbContext.SaveChangesAsync();
            return result > 0;
        }

        // UPDATE cont existent
        public async Task<bool> UpdateUserAsync(User user)
        {
            _dbContext.Users.Update(user);
            var result = await _dbContext.SaveChangesAsync();
            return result > 0;
        }

        // Ștergere permanentă cont (GDPR) — curăță în cascadă toate datele asociate, dar respectă
        // pacienții partajați cu alți utilizatori (nu îi șterge dacă mai au și alt îngrijitor)
        public async Task<bool> DeleteUserAsync(Guid id)
        {
            var user = await _dbContext.Users.FindAsync(id);
            if (user == null)
            {
                return false;
            }

            // Elimină toate legăturile user↔pacient ale acestui utilizator
            var userMonitoreds = await _dbContext.UserMonitoreds.Where(um => um.IdUser == id).ToListAsync();
            var monitoredIds = userMonitoreds.Select(um => um.IdMonitored).Distinct().ToList();
            _dbContext.UserMonitoreds.RemoveRange(userMonitoreds);

            if (monitoredIds.Count > 0)
            {
                // Excludem pacienții partajați cu alți utilizatori — un singur query batch în loc de N interogări
                var sharedIds = await _dbContext.UserMonitoreds
                    .Where(um => um.IdUser != id && monitoredIds.Contains(um.IdMonitored))
                    .Select(um => um.IdMonitored)
                    .Distinct()
                    .ToListAsync();

                // Doar pacienții exclusivi acestui utilizator (fără alți îngrijitori) sunt șterși definitiv
                var exclusiveIds = monitoredIds.Except(sharedIds).ToList();

                if (exclusiveIds.Count > 0)
                {
                    // ExecuteDeleteAsync = DELETE bulk direct în DB, fără încărcare în memorie (eficient pentru volume mari)
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

        // SELECT după token-ul de confirmare a noului email (flux schimbare email)
        public async Task<User?> GetUserByEmailChangeTokenAsync(string token)
        {
            return await _dbContext.Users.FirstOrDefaultAsync(u => u.EmailChangeToken == token);
        }

        // SELECT după token-ul de confirmare email la înregistrare
        public async Task<User?> GetUserByEmailConfirmationTokenAsync(string token)
        {
            return await _dbContext.Users.FirstOrDefaultAsync(u => u.EmailConfirmationToken == token);
        }

        // SELECT după token-ul de resetare parolă
        public async Task<User?> GetUserByPasswordResetTokenAsync(string token)
        {
            return await _dbContext.Users.FirstOrDefaultAsync(u => u.PasswordResetToken == token);
        }

        // SELECT după token-ul de anulare schimbare email (securitate — link trimis pe vechea adresă)
        public async Task<User?> GetUserByEmailChangeCancelTokenAsync(string token)
        {
            return await _dbContext.Users.FirstOrDefaultAsync(u => u.EmailChangeCancelToken == token);
        }
    }
}
