using System;
using System.Threading.Tasks;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace LifeAlertPlus.Infrastructure.Repositories
{
    // Implementare EF Core a IInvitationRepository — invitațiile trimise medicilor (acces date pacient)
    public class InvitationRepository : IInvitationRepository
    {
        private readonly LifeAlertPlusDbContext _dbContext;

        public InvitationRepository(LifeAlertPlusDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        // SELECT după token (hash SHA-256) — folosit când medicul accesează link-ul din email
        public async Task<Invitation?> GetByTokenAsync(string token)
        {
            return await _dbContext.Invitations.FirstOrDefaultAsync(i => i.Token == token);
        }

        // INSERT + commit imediat (fiecare metodă publică face propriul SaveChanges, fără Unit of Work)
        public async Task AddAsync(Invitation invitation)
        {
            await _dbContext.Invitations.AddAsync(invitation);
            await _dbContext.SaveChangesAsync();
        }

        // UPDATE + commit (ex: marcare IsAccepted=true după ce medicul acceptă invitația)
        public async Task UpdateAsync(Invitation invitation)
        {
            _dbContext.Invitations.Update(invitation);
            await _dbContext.SaveChangesAsync();
        }
    }
}