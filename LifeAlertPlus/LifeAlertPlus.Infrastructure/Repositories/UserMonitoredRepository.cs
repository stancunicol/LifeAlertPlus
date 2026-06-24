using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;
using Microsoft.EntityFrameworkCore;
using LifeAlertPlus.Infrastructure.Context;

namespace LifeAlertPlus.Infrastructure.Repositories
{
    // Implementare EF Core a IUserMonitoredRepository — relația many-to-many User ↔ Monitored
    public class UserMonitoredRepository : IUserMonitoredRepository
    {
        private readonly LifeAlertPlusDbContext _context;

        public UserMonitoredRepository(LifeAlertPlusDbContext context)
        {
            _context = context;
        }

        // SELECT toți pacienții utilizatorului prin JOIN pe tabela de legătură (fără filtrare după stare)
        public async Task<IEnumerable<Monitored>> GetMonitoredPeopleWithDetailsByUserIdAsync(Guid userId)
        {
            return await _context.UserMonitoreds
                .Where(um => um.IdUser == userId)
                .Select(um => um.Monitored)
                .ToListAsync();
        }

        // SELECT doar pacienții activi (nearchivați, fără soft-delete) — afișați pe dashboard
        public async Task<IEnumerable<Monitored>> GetActiveMonitoredPeopleByUserIdAsync(Guid userId)
        {
            return await _context.UserMonitoreds
                .Where(um => um.IdUser == userId)
                .Select(um => um.Monitored)
                .Where(m => !m.IsArchived && m.DeletedAt == null)
                .ToListAsync();
        }

        // SELECT doar pacienții arhivați (fără soft-delete) — afișați pe pagina de arhivă
        public async Task<IEnumerable<Monitored>> GetArchivedMonitoredPeopleByUserIdAsync(Guid userId)
        {
            return await _context.UserMonitoreds
                .Where(um => um.IdUser == userId)
                .Select(um => um.Monitored)
                .Where(m => m.IsArchived && m.DeletedAt == null)
                .ToListAsync();
        }

        // SELECT toate legăturile cu eager loading pe User→Role și Monitored (Admin — vizualizare globală)
        public async Task<IEnumerable<UserMonitored>> GetAllUserMonitoredWithDetailsAsync()
        {
            return await _context.UserMonitoreds
                .Include(um => um.User).ThenInclude(u => u.Role)
                .Include(um => um.Monitored)
                .ToListAsync();
        }

        // Verifică existența legăturii — folosit la autorizare (un utilizator poate accesa doar pacienții proprii)
        public async Task<bool> UserOwnsMonitoredAsync(Guid userId, Guid monitoredId) =>
            await _context.UserMonitoreds
                .AnyAsync(um => um.IdUser == userId && um.IdMonitored == monitoredId);

        // Numără câți utilizatori urmăresc acest pacient (decide dacă la ștergere se elimină doar legătura sau și pacientul)
        public async Task<int> CountUsersForMonitoredAsync(Guid monitoredId) =>
            await _context.UserMonitoreds
                .CountAsync(um => um.IdMonitored == monitoredId);

        // Elimină o legătură specifică user↔pacient (renunțare la acces / revocare de către Admin)
        public async Task RemoveUserMonitoredLinkAsync(Guid userId, Guid monitoredId)
        {
            var link = await _context.UserMonitoreds
                .FirstOrDefaultAsync(um => um.IdUser == userId && um.IdMonitored == monitoredId);
            if (link != null)
            {
                _context.UserMonitoreds.Remove(link);
                await _context.SaveChangesAsync();
            }
        }

        // Creează o legătură nouă user↔pacient — idempotent (nu duplică dacă legătura există deja)
        public async Task AddMonitoredPersonToUserAsync(Guid userId, Guid monitoredPersonId)
        {
            // Verificăm dacă relația există deja
            var exists = await _context.UserMonitoreds
                .AnyAsync(um => um.IdUser == userId && um.IdMonitored == monitoredPersonId);

            if (exists)
            {
                // Relația există deja, omitem adăugarea
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