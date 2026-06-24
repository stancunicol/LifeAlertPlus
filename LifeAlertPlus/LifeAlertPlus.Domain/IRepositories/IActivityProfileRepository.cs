using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Domain.IRepositories
{
    // Interfață repository pentru tabela ActivityProfiles (profilul comportamental orar al pacientului)
    // Profilul conține 24 înregistrări (una per oră) cu rata de mișcare, probabilitate somn și puls mediu
    // Reconstruit zilnic la 02:00 UTC de ActivityProfileRebuildBackgroundService
    public interface IActivityProfileRepository
    {
        Task<IEnumerable<ActivityProfile>> GetByMonitoredIdAsync(Guid monitoredId); // Returnează toate cele 24 ore de profil pentru o persoană
        Task UpsertAsync(ActivityProfile profile);                                   // Inserează sau actualizează profilul pentru o oră specifică (INSERT ... ON CONFLICT UPDATE)
        Task DeleteByMonitoredIdAsync(Guid monitoredId);                            // Șterge tot profilul persoanei (ex: la resetare manuală sau ștergere cont)
    }
}
