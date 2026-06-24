using System.Collections.Generic;
using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Domain.IRepositories
{
    // Interfață repository pentru tabela UserMonitoreds (relație many-to-many User ↔ Monitored)
    // Un pacient poate fi urmărit de mai mulți îngrijitori; un îngrijitor poate urmări mai mulți pacienți
    public interface IUserMonitoredRepository
    {
        Task<IEnumerable<Monitored>> GetMonitoredPeopleWithDetailsByUserIdAsync(Guid userId); // Toți pacienții utilizatorului (activi + arhivați + soft-delete)
        Task<IEnumerable<Monitored>> GetActiveMonitoredPeopleByUserIdAsync(Guid userId);      // Doar pacienții activi (fără arhivați sau cu DeletedAt) — folosit pe dashboard
        Task<IEnumerable<Monitored>> GetArchivedMonitoredPeopleByUserIdAsync(Guid userId);    // Doar pacienții arhivați — folosit pe pagina de arhivă
        Task<IEnumerable<UserMonitored>> GetAllUserMonitoredWithDetailsAsync();               // Toate legăturile user↔pacient (Admin — vizualizare globală)
        Task AddMonitoredPersonToUserAsync(Guid userId, Guid monitoredPersonId);              // Creează o legătură nouă (acordă acces utilizatorului la pacient)
        Task<bool> UserOwnsMonitoredAsync(Guid userId, Guid monitoredId);                    // Verifică dacă utilizatorul are acces la pacient (autorizare în controllere)
        Task<int> CountUsersForMonitoredAsync(Guid monitoredId);                             // Câți utilizatori urmăresc pacientul (dacă > 1, ștergerea elimină doar legătura)
        Task RemoveUserMonitoredLinkAsync(Guid userId, Guid monitoredId);                    // Elimină legătura (utilizator renunță la acces sau Admin revocă)
    }
}