using System;
using System.Threading.Tasks;
using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Domain.IRepositories
{
    // Interfață repository pentru tabela Invitations (invitații trimise medicilor pentru acces la datele pacientului)
    // Token-ul stocat în DB este hash SHA-256 al token-ului brut trimis în URL (securitate)
    public interface IInvitationRepository
    {
        Task<Invitation?> GetByTokenAsync(string token); // Caută invitația după hash-ul SHA-256 al token-ului (nu token-ul brut)
        Task AddAsync(Invitation invitation);             // Inserează o invitație nouă (cu expirare 24 ore)
        Task UpdateAsync(Invitation invitation);          // Actualizează invitația (ex: IsAccepted=true după ce medicul acceptă)
    }
}