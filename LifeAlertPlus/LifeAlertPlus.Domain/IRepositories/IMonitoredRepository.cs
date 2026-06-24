using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Domain.IRepositories
{
    // Interfață repository pentru tabela Monitoreds (pacienții monitorizați cu dispozitiv ESP32)
    // Suportă ciclul complet: creare → arhivare → soft-delete (grație 7 zile) → ștergere permanentă
    public interface IMonitoredRepository
    {
        Task<Monitored> AddMonitoredPersonAsync(Monitored monitoredPerson);                       // Inserează o persoană monitorizată nouă și returnează entitatea cu ID generat
        Task<Monitored?> GetMonitoredPersonByDeviceSerialNumberAsync(string deviceSerialNumber);  // Caută după seria dispozitivului ESP32 (folosit la ingestia datelor din ESPController)
        Task<Monitored?> GetMonitoredPersonByIdAsync(Guid id);                                   // Caută după ID (GUID)
        Task<IEnumerable<Monitored>> GetAllMonitoredPeopleAsync();                               // Returnează toate persoanele din sistem (Admin)
        Task UpdateMonitoredPersonAsync(Monitored monitoredPerson);                              // Salvează modificările (praguri, afecțiuni, date personale)
        Task DeleteMonitoredPersonAsync(Guid id);                                                // Hard-delete permanent (numai după arhivare prealabilă)
        Task<bool> ArchiveMonitoredPersonAsync(Guid id, DateTime archivedAt);                   // Setează IsArchived=true și ArchivedAt (oprire monitorizare, date păstrate)
        Task<bool> RestoreMonitoredPersonAsync(Guid id);                                        // Curăță IsArchived și ArchivedAt (reactivare monitorizare)
        Task<bool> SoftDeleteMonitoredPersonAsync(Guid id, DateTime deletedAt);                 // Setează DeletedAt (grație 7 zile înainte de ștergere permanentă)
        Task<bool> ReactivateMonitoredPersonAsync(Guid id);                                     // Curăță DeletedAt (Admin anulează soft-delete înainte de expirarea grației)
    }
}