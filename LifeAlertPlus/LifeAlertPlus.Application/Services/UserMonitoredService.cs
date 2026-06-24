using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Application.IServices;

namespace LifeAlertPlus.Application.Services
{
    // Serviciu pentru relația many-to-many User ↔ Monitored (tabelă UserMonitoreds)
    // Un pacient poate fi urmărit de mai mulți îngrijitori; un îngrijitor poate urmări mai mulți pacienți
    // Toate metodele delegă direct la IUserMonitoredRepository
    public class UserMonitoredService : IUserMonitoredService
    {
        private readonly IUserMonitoredRepository _userMonitoredRepository;

        public UserMonitoredService(IUserMonitoredRepository userMonitoredRepository)
        {
            _userMonitoredRepository = userMonitoredRepository;
        }

        // Toate persoanele monitorizate ale utilizatorului (activi + arhivați + în soft-delete)
        public async Task<IEnumerable<Monitored>> GetMonitoredPeopleByUserIdAsync(Guid userId)
        {
            return await _userMonitoredRepository.GetMonitoredPeopleWithDetailsByUserIdAsync(userId);
        }

        // Doar persoanele active (nearchivate, fără DeletedAt) — afișate pe dashboard principal
        public async Task<IEnumerable<Monitored>> GetActiveMonitoredPeopleByUserIdAsync(Guid userId)
        {
            return await _userMonitoredRepository.GetActiveMonitoredPeopleByUserIdAsync(userId);
        }

        // Doar persoanele arhivate — afișate pe pagina de arhivă
        public async Task<IEnumerable<Monitored>> GetArchivedMonitoredPeopleByUserIdAsync(Guid userId)
        {
            return await _userMonitoredRepository.GetArchivedMonitoredPeopleByUserIdAsync(userId);
        }

        // Toate legăturile user↔monitored cu detalii (Admin — vizualizare globală a asocierilor)
        public async Task<IEnumerable<UserMonitored>> GetAllUserMonitoredWithDetailsAsync()
        {
            return await _userMonitoredRepository.GetAllUserMonitoredWithDetailsAsync();
        }

        // Acordă unui utilizator acces la o persoană monitorizată (creare legătură în UserMonitoreds)
        public async Task AddMonitoredPersonToUserAsync(Guid userId, Guid monitoredPersonId)
        {
            await _userMonitoredRepository.AddMonitoredPersonToUserAsync(userId, monitoredPersonId);
        }

        // Verifică dacă utilizatorul are acces la persoana monitorizată — folosit la autorizare în controllere
        public async Task<bool> UserOwnsMonitoredAsync(Guid userId, Guid monitoredId) =>
            await _userMonitoredRepository.UserOwnsMonitoredAsync(userId, monitoredId);

        // Numără câți utilizatori urmăresc această persoană — dacă > 1, la ștergere se elimină doar legătura
        public async Task<int> CountUsersForMonitoredAsync(Guid monitoredId) =>
            await _userMonitoredRepository.CountUsersForMonitoredAsync(monitoredId);

        // Elimină legătura user↔monitored fără a șterge persoana (co-proprietar renunță la acces)
        public async Task RemoveUserMonitoredLinkAsync(Guid userId, Guid monitoredId) =>
            await _userMonitoredRepository.RemoveUserMonitoredLinkAsync(userId, monitoredId);
    }
}