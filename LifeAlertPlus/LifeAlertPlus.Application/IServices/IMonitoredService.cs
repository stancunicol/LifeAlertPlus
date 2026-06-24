using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Shared.DTOs.Requests.Monitored;

namespace LifeAlertPlus.Application.IServices
{
    // Interfață pentru gestionarea persoanelor monitorizate (pacienți cu dispozitiv ESP32)
    // Suportă ciclul complet: creare → arhivare → soft-delete (7 zile grație) → ștergere permanentă
    public interface IMonitoredService
    {
        Task<Monitored> AddMonitoredPersonAsync(MonitorCreateRequestDTO monitoredPersonDto);            // Creează o persoană monitorizată nouă și o asociază cu dispozitivul ESP32
        Task<Monitored?> GetMonitoredPersonByDeviceSerialNumberAsync(string deviceSerialNumber);       // Caută persoana după numărul de serie al dispozitivului (folosit de ESP la ingest)
        Task<Monitored?> GetMonitoredPersonByIdAsync(Guid id);                                        // Caută persoana după ID
        Task<IEnumerable<Monitored>> GetAllMonitoredPeopleAsync();                                    // Returnează toate persoanele monitorizate (Admin)
        Task UpdateMonitoredPersonAsync(Monitored monitoredPerson);                                   // Actualizează datele persoanei (nume, praguri, afecțiuni etc.)
        Task DeleteMonitoredPersonAsync(Guid id);                                                     // Ștergere permanentă (hard-delete) — doar după arhivare
        Task<bool> ArchiveMonitoredPersonAsync(Guid id);                                             // Arhivare: oprește monitorizarea activă, păstrează datele istorice
        Task<bool> RestoreMonitoredPersonAsync(Guid id);                                             // Dezarhivare: reia monitorizarea activă
        Task<bool> SoftDeleteMonitoredPersonAsync(Guid id);                                          // Soft-delete: marchează DeletedAt, date șterse după 7 zile grație
        Task<bool> ReactivateMonitoredPersonAsync(Guid id);                                          // Anulează soft-delete (Admin): șterge DeletedAt înainte de expirarea grației
    }
}