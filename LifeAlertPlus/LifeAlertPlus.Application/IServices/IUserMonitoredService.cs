using System;
using System.Collections.Generic;
using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Application.IServices
{
    // Interfață pentru gestionarea relației many-to-many între utilizatori și persoane monitorizate
    // Un pacient poate fi urmărit de mai mulți îngrijitori; un îngrijitor poate urmări mai mulți pacienți
    public interface IUserMonitoredService
    {
        Task<IEnumerable<Monitored>> GetMonitoredPeopleByUserIdAsync(Guid userId);             // Toate persoanele monitorizate ale unui utilizator (activi + arhivați)
        Task<IEnumerable<Monitored>> GetActiveMonitoredPeopleByUserIdAsync(Guid userId);       // Doar persoanele active (nearchivate, neșterse) ale unui utilizator
        Task<IEnumerable<Monitored>> GetArchivedMonitoredPeopleByUserIdAsync(Guid userId);     // Doar persoanele arhivate ale unui utilizator
        Task<IEnumerable<UserMonitored>> GetAllUserMonitoredWithDetailsAsync();                // Toate legăturile user↔monitored cu detalii (Admin — vizualizare globală)
        Task AddMonitoredPersonToUserAsync(Guid userId, Guid monitoredPersonId);              // Creează legătura user↔monitored (acordă acces îngrijitorului la pacient)
        Task<bool> UserOwnsMonitoredAsync(Guid userId, Guid monitoredId);                     // Verifică dacă utilizatorul are acces la persoana monitorizată (autorizare)
        Task<int> CountUsersForMonitoredAsync(Guid monitoredId);                              // Numărul de utilizatori care urmăresc această persoană (decide dacă se poate șterge sau doar dezlega)
        Task RemoveUserMonitoredLinkAsync(Guid userId, Guid monitoredId);                     // Elimină legătura user↔monitored fără a șterge persoana (co-proprietar renunță la acces)
    }
}