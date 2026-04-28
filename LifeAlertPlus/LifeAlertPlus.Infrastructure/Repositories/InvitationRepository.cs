using System;
using System.Threading.Tasks;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace LifeAlertPlus.Infrastructure.Repositories
{
    public class InvitationRepository : IInvitationRepository
    {
        private readonly LifeAlertPlusDbContext _dbContext;

        public InvitationRepository(LifeAlertPlusDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<Invitation?> GetByTokenAsync(string token)
        {
            return await _dbContext.Invitations.FirstOrDefaultAsync(i => i.Token == token);
        }

        public async Task AddAsync(Invitation invitation)
        {
            await _dbContext.Invitations.AddAsync(invitation);
            await _dbContext.SaveChangesAsync();
        }

        public async Task UpdateAsync(Invitation invitation)
        {
            _dbContext.Invitations.Update(invitation);
            await _dbContext.SaveChangesAsync();
        }
    }
}