using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Shared.DTOs.Requests.Monitored;

namespace LifeAlertPlus.Application.Services
{
    // Serviciu pentru gestionarea persoanelor monitorizate (pacienți cu dispozitiv ESP32)
    // Gestionează ciclul complet: creare → arhivare → soft-delete (grație 7 zile) → ștergere permanentă
    public class MonitoredService : IMonitoredService
    {
        private readonly IMonitoredRepository _monitoredRepository;

        public MonitoredService(IMonitoredRepository monitoredRepository)
        {
            _monitoredRepository = monitoredRepository;
        }

        // Creează o persoană monitorizată nouă cu praguri vitale implicite (ajustate ulterior de ConditionThresholdAdjuster)
        public async Task<Monitored> AddMonitoredPersonAsync(MonitorCreateRequestDTO monitoredPersonDto)
        {
            var monitoredPerson = new Monitored
            {
                Id                 = Guid.NewGuid(),
                FirstName          = monitoredPersonDto.FirstName,
                LastName           = monitoredPersonDto.LastName,
                Birthdate          = monitoredPersonDto.Birthdate,
                Gender             = monitoredPersonDto.Gender,
                Address            = monitoredPersonDto.Address,
                DeviceSerialNumber = monitoredPersonDto.DeviceSerialNumber,
                // Praguri vitale implicite — vor fi recalculate la adăugarea afecțiunilor
                MinHeartRate   = 60,
                MaxHeartRate   = 100,
                MinTemperature = 36.0,
                MaxTemperature = 37.5,
                CreatedAt      = DateTime.UtcNow
            };

            return await _monitoredRepository.AddMonitoredPersonAsync(monitoredPerson);
        }

        // Ștergere permanentă (hard-delete) — numai după ce persoana a fost arhivată anterior
        public async Task DeleteMonitoredPersonAsync(Guid id)
        {
            await _monitoredRepository.DeleteMonitoredPersonAsync(id);
        }

        // Returnează toate persoanele monitorizate din sistem (folosit de Admin)
        public async Task<IEnumerable<Monitored>> GetAllMonitoredPeopleAsync()
        {
            return await _monitoredRepository.GetAllMonitoredPeopleAsync();
        }

        // Caută persoana după numărul de serie al dispozitivului (folosit de ESP la trimiterea datelor)
        public async Task<Monitored?> GetMonitoredPersonByDeviceSerialNumberAsync(string deviceSerialNumber)
        {
            return await _monitoredRepository.GetMonitoredPersonByDeviceSerialNumberAsync(deviceSerialNumber);
        }

        // Caută persoana după ID (GUID)
        public async Task<Monitored?> GetMonitoredPersonByIdAsync(Guid id)
        {
            return await _monitoredRepository.GetMonitoredPersonByIdAsync(id);
        }

        // Salvează modificările persoanei monitorizate (nume, praguri, afecțiuni etc.)
        public async Task UpdateMonitoredPersonAsync(Monitored monitoredPerson)
        {
            await _monitoredRepository.UpdateMonitoredPersonAsync(monitoredPerson);
        }

        // Arhivare: oprește monitorizarea activă, setează IsArchived=true și ArchivedAt=now
        // Datele istorice rămân în DB pentru ArchiveRetentionDays zile
        public async Task<bool> ArchiveMonitoredPersonAsync(Guid id)
        {
            return await _monitoredRepository.ArchiveMonitoredPersonAsync(id, DateTime.UtcNow);
        }

        // Dezarhivare: curăță IsArchived și ArchivedAt → persoana revine în monitorizare activă
        public async Task<bool> RestoreMonitoredPersonAsync(Guid id)
        {
            return await _monitoredRepository.RestoreMonitoredPersonAsync(id);
        }

        // Soft-delete: marchează DeletedAt=now, datele sunt șterse automat după 7 zile (GracePeriodDays)
        public async Task<bool> SoftDeleteMonitoredPersonAsync(Guid id)
        {
            return await _monitoredRepository.SoftDeleteMonitoredPersonAsync(id, DateTime.UtcNow);
        }

        // Reactivare (Admin): anulează soft-delete înainte de expirarea grației (curăță DeletedAt)
        public async Task<bool> ReactivateMonitoredPersonAsync(Guid id)
        {
            return await _monitoredRepository.ReactivateMonitoredPersonAsync(id);
        }
    }
}