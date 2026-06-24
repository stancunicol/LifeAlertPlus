using LifeAlertPlus.API.Helpers;
using LifeAlertPlus.API.Services;
using LifeAlertPlus.Shared.DTOs.Requests.Monitored;
using LifeAlertPlus.Application.IServices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.Extensions.Logging;

namespace LifeAlertPlus.API.Controllers
{
    // Controller pentru gestionarea persoanelor monitorizate (pacienți purtători de dispozitiv ESP32).
    // Ciclul de viață al unei persoane monitorizate:
    // Activ → Arhivat (date păstrate, fără alertă) → Șters (soft) → Șters definitiv (după 7 zile)
    // sau Activ → Reactivat (anulare ștergere, Admin only)
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class MonitoredController : BaseApiController
    {
        private readonly IMonitoredService _monitoredService;          // Logica CRUD persoane monitorizate
        private readonly IUserMonitoredService _userMonitoredService;  // Gestionarea legăturilor User-Monitored
        private readonly AlertMonitorService _alertMonitorService;     // Invalidare cache la arhivare
        private readonly ILogger<MonitoredController> _logger;
        private readonly AuditService _auditService;                   // Log de audit pentru acțiuni importante

        public MonitoredController(IMonitoredService monitoredService, IUserMonitoredService userMonitoredService, AlertMonitorService alertMonitorService, ILogger<MonitoredController> logger, AuditService auditService)
        {
            _monitoredService     = monitoredService;
            _userMonitoredService = userMonitoredService;
            _alertMonitorService  = alertMonitorService;
            _logger               = logger;
            _auditService         = auditService;
        }

        // POST /api/monitored/add — Adaugă o persoană monitorizată și o leagă de utilizatorul curent
        // Comportament smart: dacă numărul de serie există deja → leagă utilizatorul la persoana existentă
        // (același dispozitiv poate fi monitorizat de mai mulți îngrijitori)
        [HttpPost("add")]
        public async Task<IActionResult> AddMonitoredPerson([FromBody] MonitorAddRequestDTO newMonitored)
        {
            var callerIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (callerIdStr == null || !Guid.TryParse(callerIdStr, out var callerId))
                return Unauthorized(new { Message = ResponseMessages.InvalidToken });

            var newPerson = newMonitored.MonitoredPerson;

            if (newPerson == null)
                return BadRequest(new { Message = "Invalid monitored person data." });

            // Validăm câmpurile obligatorii
            if (string.IsNullOrEmpty(newPerson.FirstName) || string.IsNullOrEmpty(newPerson.LastName) ||
                string.IsNullOrEmpty(newPerson.DeviceSerialNumber) || string.IsNullOrEmpty(newPerson.Address) ||
                string.IsNullOrEmpty(newPerson.Gender))
            {
                return BadRequest(new { Message = "All fields are required." });
            }

            // Verificăm dacă numărul de serie este deja înregistrat
            var existingPerson = await _monitoredService.GetMonitoredPersonByDeviceSerialNumberAsync(newPerson.DeviceSerialNumber);
            if (existingPerson != null)
            {
                if (existingPerson.DeletedAt != null)
                    return Conflict(new { Message = "A monitored person with this device serial number is pending deletion." }); // Așteptăm curățarea

                // Persoana există și e activă → legăm utilizatorul curent la ea (fără a crea duplicat)
                await _userMonitoredService.AddMonitoredPersonToUserAsync(callerId, existingPerson.Id);
                _logger.LogInformation("User {UserId} linked to existing monitored {MonitoredId} via add", callerId, existingPerson.Id);
                _auditService.LogAsync(User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? callerId.ToString(),
                    "LinkPatient", $"User linked to existing patient {existingPerson.FirstName} {existingPerson.LastName} (id={existingPerson.Id})", "Patient");
                return Ok(new { Message = "Monitored person added successfully.", MonitoredPerson = existingPerson });
            }

            try
            {
                // Creăm persoana nouă în DB
                var createdPerson = await _monitoredService.AddMonitoredPersonAsync(newPerson);
                if (createdPerson == null)
                    return StatusCode(500, new { Message = "Failed to add monitored person." });

                createdPerson.IsActive = true; // Marcăm persoana ca activă imediat după creare
                // Creăm legătura User-Monitored (îngrijitorul are acces la persoana nouă)
                await _userMonitoredService.AddMonitoredPersonToUserAsync(callerId, createdPerson.Id);

                return Ok(new { Message = "Monitored person added successfully.", MonitoredPerson = createdPerson });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding monitored person for user {UserId}", callerId);
                return StatusCode(500, new { Message = "An error occurred while adding monitored person." });
            }
        }

        // GET /api/monitored/serial/{deviceSerialNumber} — Caută persoana după numărul de serie al dispozitivului
        // Folosit de admin pentru debug/diagnosticare dispozitive
        [Authorize(Roles = "Admin")]
        [HttpGet("serial/{deviceSerialNumber}")]
        public async Task<IActionResult> GetMonitoredPersonByDeviceSerialNumber([FromRoute] string deviceSerialNumber)
        {
            var monitoredPerson = await _monitoredService.GetMonitoredPersonByDeviceSerialNumberAsync(deviceSerialNumber);
            if (monitoredPerson == null)
                return NotFound(new { Message = ResponseMessages.MonitoredPersonNotFound });

            return Ok(monitoredPerson);
        }

        // GET /api/monitored/id/{id} — Returnează datele unei persoane monitorizate după ID
        [HttpGet("id/{id:guid}")]
        public async Task<IActionResult> GetMonitoredPersonById([FromRoute] Guid id)
        {
            var callerId = GetCallerId();
            if (callerId == null)
                return Unauthorized(new { Message = ResponseMessages.InvalidToken });

            var monitoredPerson = await _monitoredService.GetMonitoredPersonByIdAsync(id);
            if (monitoredPerson == null)
                return NotFound(new { Message = ResponseMessages.MonitoredPersonNotFound });

            // Verificăm că utilizatorul curent are acces la această persoană
            if (!IsAdminRole() && !await _userMonitoredService.UserOwnsMonitoredAsync(callerId.Value, id))
                return Forbid();

            return Ok(monitoredPerson);
        }

        // PUT /api/monitored/update/{id} — Actualizează datele persoanei monitorizate și pragurile de alertă
        [HttpPut("update/{id:guid}")]
        public async Task<IActionResult> UpdateMonitoredPerson([FromRoute] Guid id, [FromBody] MonitorUpdateRequestDTO dto)
        {
            var callerIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (callerIdStr == null || !Guid.TryParse(callerIdStr, out var callerId))
                return Unauthorized(new { Message = ResponseMessages.InvalidToken });

            var existing = await _monitoredService.GetMonitoredPersonByIdAsync(id);
            if (existing == null)
                return NotFound(new { Message = ResponseMessages.MonitoredPersonNotFound });

            if (!IsAdminRole() && !await _userMonitoredService.UserOwnsMonitoredAsync(callerId, id))
                return Forbid();

            // Dacă numărul de serie se schimbă, verificăm că noul număr nu e ocupat de altă persoană
            if (dto.DeviceSerialNumber != existing.DeviceSerialNumber)
            {
                var conflict = await _monitoredService.GetMonitoredPersonByDeviceSerialNumberAsync(dto.DeviceSerialNumber);
                if (conflict != null && conflict.Id != id)
                    return Conflict(new { Message = "A monitored person with this device serial number already exists." });
            }

            // Actualizăm toți câmpurile (actualizare completă, nu parțială)
            existing.FirstName            = dto.FirstName;
            existing.LastName             = dto.LastName;
            existing.Birthdate            = dto.Birthdate;
            existing.Gender               = dto.Gender;
            existing.Address              = dto.Address;
            existing.DeviceSerialNumber   = dto.DeviceSerialNumber;
            // Praguri personalizate de alertă (suprascriu valorile implicite ale utilizatorului)
            existing.MinHeartRate         = dto.MinHeartRate;
            existing.MaxHeartRate         = dto.MaxHeartRate;
            existing.MinTemperature       = dto.MinTemperature;
            existing.MaxTemperature       = dto.MaxTemperature;
            existing.MinSpO2              = dto.MinSpO2;
            existing.MaxSpO2              = dto.MaxSpO2;
            existing.UpdateFrequency      = dto.UpdateFrequency;     // Frecvența de trimitere date (secunde)
            existing.DataRetentionDays    = dto.DataRetentionDays;   // Câte zile se păstrează măsurătorile
            existing.ArchiveRetentionDays = dto.ArchiveRetentionDays; // Câte zile se păstrează datele arhivate
            existing.UpdatedAt            = DateTime.UtcNow;

            await _monitoredService.UpdateMonitoredPersonAsync(existing);

            return Ok(new { Message = "Monitored person updated successfully.", MonitoredPerson = existing });
        }

        // PUT /api/monitored/archive/{id} — Arhivează persoana monitorizată
        // Persoana arhivată: datele istorice se păstrează, dar nu mai generează alerte
        // Util când pacientul e externat sau nu mai poartă dispozitivul temporar
        [HttpPut("archive/{id:guid}")]
        public async Task<IActionResult> ArchiveMonitoredPerson([FromRoute] Guid id)
        {
            var callerId = GetCallerId();
            if (callerId == null)
                return Unauthorized(new { Message = ResponseMessages.InvalidToken });

            var existing = await _monitoredService.GetMonitoredPersonByIdAsync(id);
            if (existing == null)
                return NotFound(new { Message = ResponseMessages.MonitoredPersonNotFound });

            if (!IsAdminRole() && !await _userMonitoredService.UserOwnsMonitoredAsync(callerId.Value, id))
                return Forbid();

            if (existing.IsArchived)
                return Ok(new { Message = "Monitored person is already archived.", MonitoredPerson = existing }); // Idempotent

            var ok = await _monitoredService.ArchiveMonitoredPersonAsync(id);
            if (!ok)
                return StatusCode(500, new { Message = "Failed to archive monitored person." });

            // Invalidăm cache-ul de stare arhivat din AlertMonitorService
            // (ca să nu mai procesăm alerte pentru această persoană)
            _alertMonitorService.InvalidateArchivedCache(id);
            _logger.LogInformation("User {UserId} archived monitored {MonitoredId}", callerId, id);
            _auditService.LogAsync(User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? callerId!.Value.ToString(),
                "ArchivePatient", $"Archived patient {existing.FirstName} {existing.LastName} (id={id})", "Patient");
            return Ok(new { Message = "Monitored person archived successfully." });
        }

        // PUT /api/monitored/restore/{id} — Restaurează persoana din arhivă
        // Persoana restaurată devine activă din nou (AlertMonitorService reîncepe să proceseze date)
        [HttpPut("restore/{id:guid}")]
        public async Task<IActionResult> RestoreMonitoredPerson([FromRoute] Guid id)
        {
            var callerId = GetCallerId();
            if (callerId == null)
                return Unauthorized(new { Message = ResponseMessages.InvalidToken });

            var existing = await _monitoredService.GetMonitoredPersonByIdAsync(id);
            if (existing == null)
                return NotFound(new { Message = ResponseMessages.MonitoredPersonNotFound });

            if (!IsAdminRole() && !await _userMonitoredService.UserOwnsMonitoredAsync(callerId.Value, id))
                return Forbid();

            if (!existing.IsArchived)
                return Ok(new { Message = "Monitored person is not archived.", MonitoredPerson = existing }); // Idempotent

            var ok = await _monitoredService.RestoreMonitoredPersonAsync(id);
            if (!ok)
                return StatusCode(500, new { Message = "Failed to restore monitored person." });

            // Invalidăm cache-ul de arhivare → AlertMonitorService va procesa din nou datele
            _alertMonitorService.InvalidateArchivedCache(id);
            _logger.LogInformation("User {UserId} restored monitored {MonitoredId} from archive", callerId, id);
            _auditService.LogAsync(User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? callerId!.Value.ToString(),
                "RestorePatient", $"Restored patient {existing.FirstName} {existing.LastName} (id={id}) from archive", "Patient");
            return Ok(new { Message = "Monitored person restored successfully." });
        }

        // DELETE /api/monitored/{id}/remove — Îndepărtare inteligentă a persoanei monitorizate
        // Comportament bazat pe numărul de proprietari:
        //   - Admin: soft-delete întotdeauna (DeletedAt setat; ștergere permanentă după 7 zile)
        //   - Utilizator care e singurul proprietar: soft-delete (wasLastOwner=true)
        //   - Utilizator cu alți co-proprietari: elimină doar legătura sa (wasLastOwner=false, persoana rămâne)
        [HttpDelete("{id:guid}/remove")]
        public async Task<IActionResult> RemoveMonitoredPerson([FromRoute] Guid id)
        {
            var callerId = GetCallerId();
            if (callerId == null)
                return Unauthorized(new { Message = ResponseMessages.InvalidToken });

            var existing = await _monitoredService.GetMonitoredPersonByIdAsync(id);
            if (existing == null)
                return NotFound(new { Message = ResponseMessages.MonitoredPersonNotFound });

            bool isAdmin = IsAdminRole();

            if (!isAdmin && !await _userMonitoredService.UserOwnsMonitoredAsync(callerId.Value, id))
                return Forbid();

            if (isAdmin)
            {
                // Adminul forțează soft-delete indiferent de câți proprietari există
                var ok = await _monitoredService.SoftDeleteMonitoredPersonAsync(id);
                if (!ok) return StatusCode(500, new { Message = "Failed to delete monitored person." });
                _alertMonitorService.InvalidateArchivedCache(id); // Nu mai procesăm alerte
                _logger.LogWarning("Admin {UserId} soft-deleted monitored {MonitoredId}", callerId, id);
                _auditService.LogAsync(User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? callerId!.Value.ToString(),
                    "SoftDeletePatient", $"Admin soft-deleted patient {existing.FirstName} {existing.LastName} (id={id})", "Patient");
                return Ok(new { wasLastOwner = true, message = "Persoana monitorizată a fost marcată pentru ștergere și va fi eliminată definitiv după 7 zile." });
            }

            // Numărăm câți utilizatori mai au acces la această persoană
            var ownerCount = await _userMonitoredService.CountUsersForMonitoredAsync(id);
            if (ownerCount > 1)
            {
                // Există alți îngrijitori → eliminăm doar legătura utilizatorului curent
                await _userMonitoredService.RemoveUserMonitoredLinkAsync(callerId.Value, id);
                _logger.LogInformation("User {UserId} unlinked from monitored {MonitoredId} ({Count} owners remain)", callerId, id, ownerCount - 1);
                _auditService.LogAsync(User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? callerId!.Value.ToString(),
                    "UnlinkPatient", $"User unlinked from patient {existing.FirstName} {existing.LastName} (id={id})", "Patient");
                return Ok(new { wasLastOwner = false, message = "Ai fost deconectat de la această persoană monitorizată." });
            }
            else
            {
                // Utilizatorul este singurul proprietar → soft-delete (date păstrate 7 zile)
                var ok = await _monitoredService.SoftDeleteMonitoredPersonAsync(id);
                if (!ok) return StatusCode(500, new { Message = "Failed to delete monitored person." });
                _alertMonitorService.InvalidateArchivedCache(id);
                _logger.LogWarning("User {UserId} soft-deleted monitored {MonitoredId} (was last owner)", callerId, id);
                _auditService.LogAsync(User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? callerId!.Value.ToString(),
                    "SoftDeletePatient", $"Soft-deleted patient {existing.FirstName} {existing.LastName} (id={id})", "Patient");
                return Ok(new { wasLastOwner = true, message = "Persoana monitorizată a fost marcată pentru ștergere și va fi eliminată definitiv după 7 zile." });
            }
        }

        // PUT /api/monitored/reactivate/{id} — Admin anulează soft-delete (Admin only)
        // Permite recuperarea unei persoane marcate pentru ștergere, în perioada de grație de 7 zile
        [Authorize(Roles = "Admin")]
        [HttpPut("reactivate/{id:guid}")]
        public async Task<IActionResult> ReactivateMonitoredPerson([FromRoute] Guid id)
        {
            var callerId = GetCallerId();
            if (callerId == null)
                return Unauthorized(new { Message = ResponseMessages.InvalidToken });

            var existing = await _monitoredService.GetMonitoredPersonByIdAsync(id);
            if (existing == null)
                return NotFound(new { Message = ResponseMessages.MonitoredPersonNotFound });

            if (existing.DeletedAt == null)
                return BadRequest(new { Message = "Persoana monitorizată nu este marcată pentru ștergere." }); // Nu e în soft-delete

            var ok = await _monitoredService.ReactivateMonitoredPersonAsync(id);
            if (!ok) return StatusCode(500, new { Message = "Failed to reactivate monitored person." });

            _alertMonitorService.InvalidateArchivedCache(id);
            _logger.LogInformation("Admin {UserId} reactivated monitored {MonitoredId}", callerId, id);
            _auditService.LogAsync(User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? callerId!.Value.ToString(),
                "ReactivatePatient", $"Reactivated patient {existing.FirstName} {existing.LastName} (id={id})", "Patient");
            return Ok(new { Message = "Persoana monitorizată a fost reactivată." });
        }

        // DELETE /api/monitored/{id} — Ștergere permanentă (hard delete)
        // PROTECȚIE: Poate fi apelat NUMAI pentru persoanele arhivate (IsArchived=true)
        // Previne ștergerea accidentală a pacienților activi
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> DeleteMonitoredPerson([FromRoute] Guid id)
        {
            var callerId = GetCallerId();
            if (callerId == null)
                return Unauthorized(new { Message = ResponseMessages.InvalidToken });

            var existing = await _monitoredService.GetMonitoredPersonByIdAsync(id);
            if (existing == null)
                return NotFound(new { Message = ResponseMessages.MonitoredPersonNotFound });

            if (!IsAdminRole() && !await _userMonitoredService.UserOwnsMonitoredAsync(callerId.Value, id))
                return Forbid();

            // Ștergerea permanentă este permisă NUMAI din arhivă
            // Fluxul corect: Activ → Arhivat → Șters permanent
            if (!existing.IsArchived)
                return BadRequest(new { Message = "Person must be archived before permanent deletion." });

            await _monitoredService.DeleteMonitoredPersonAsync(id); // Șterge complet din DB (cascade)
            _logger.LogWarning("User {UserId} permanently deleted monitored {MonitoredId}", callerId, id);
            _auditService.LogAsync(User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? callerId!.Value.ToString(),
                "DeletePatient", $"Permanently deleted patient {existing.FirstName} {existing.LastName} (id={id})", "Patient");
            return Ok(new { Message = "Monitored person permanently deleted." });
        }
    }
}