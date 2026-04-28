using System;
using System.Threading.Tasks;
using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Domain.IRepositories
{
    public interface IInvitationRepository
    {
        Task<Invitation?> GetByTokenAsync(string token);
        Task AddAsync(Invitation invitation);
        Task UpdateAsync(Invitation invitation);
    }
}