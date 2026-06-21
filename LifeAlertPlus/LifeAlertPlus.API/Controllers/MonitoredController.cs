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
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class MonitoredController : BaseApiController
    {
        private readonly IMonitoredService _monitoredService;
        private readonly IUserMonitoredService _userMonitoredService;
        private readonly AlertMonitorService _alertMonitorService;
        private readonly ILogger<MonitoredController> _logger;
        private readonly AuditService _auditService;

        public MonitoredController(IMonitoredService monitoredService, IUserMonitoredService userMonitoredService, AlertMonitorService alertMonitorService, ILogger<MonitoredController> logger, AuditService auditService)
        {
            _monitoredService = monitoredService;
            _userMonitoredService = userMonitoredService;
            _alertMonitorService = alertMonitorService;
            _logger = logger;
            _auditService = auditService;
        }

        [HttpPost("add")]
        public async Task<IActionResult> AddMonitoredPerson([FromBody] MonitorAddRequestDTO newMonitored)
        {
            var callerIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (callerIdStr == null || !Guid.TryParse(callerIdStr, out var callerId))
                return Unauthorized(new { Message = ResponseMessages.InvalidToken });

            var newPerson = newMonitored.MonitoredPerson;

            if(newPerson == null)
            {
                return BadRequest(new { Message = "Invalid monitored person data." });
            }

            if(string.IsNullOrEmpty(newPerson.FirstName) || string.IsNullOrEmpty(newPerson.LastName) || 
            string.IsNullOrEmpty(newPerson.DeviceSerialNumber) || string.IsNullOrEmpty(newPerson.Address) ||
            string.IsNullOrEmpty(newPerson.Gender))
            {
                return BadRequest(new { Message = "All fields are required." });
            }

            var existingPerson = await _monitoredService.GetMonitoredPersonByDeviceSerialNumberAsync(newPerson.DeviceSerialNumber);
            if (existingPerson != null)
            {
                if (existingPerson.DeletedAt != null)
                    return Conflict(new { Message = "A monitored person with this device serial number is pending deletion." });

                // Person exists and is active — silently link the caller to it.
                await _userMonitoredService.AddMonitoredPersonToUserAsync(callerId, existingPerson.Id);
                _logger.LogInformation("User {UserId} linked to existing monitored {MonitoredId} via add", callerId, existingPerson.Id);
                _auditService.LogAsync(User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? callerId.ToString(),
                    "LinkPatient", $"User linked to existing patient {existingPerson.FirstName} {existingPerson.LastName} (id={existingPerson.Id})", "Patient");
                return Ok(new { Message = "Monitored person added successfully.", MonitoredPerson = existingPerson });
            }

            try
            {
                var createdPerson = await _monitoredService.AddMonitoredPersonAsync(newPerson);
                if (createdPerson == null)
                {
                    return StatusCode(500, new { Message = "Failed to add monitored person." });
                }

                createdPerson.IsActive = true;
                await _userMonitoredService.AddMonitoredPersonToUserAsync(callerId, createdPerson.Id);

                return Ok(new { Message = "Monitored person added successfully.", MonitoredPerson = createdPerson });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding monitored person for user {UserId}", callerId);
                return StatusCode(500, new { Message = "An error occurred while adding monitored person." });
            }
        }

        [Authorize(Roles = "Admin")]
        [HttpGet("serial/{deviceSerialNumber}")]
        public async Task<IActionResult> GetMonitoredPersonByDeviceSerialNumber([FromRoute] string deviceSerialNumber)
        {
            var monitoredPerson = await _monitoredService.GetMonitoredPersonByDeviceSerialNumberAsync(deviceSerialNumber);
            if (monitoredPerson == null)
                return NotFound(new { Message = ResponseMessages.MonitoredPersonNotFound });

            return Ok(monitoredPerson);
        }

        [HttpGet("id/{id:guid}")]
        public async Task<IActionResult> GetMonitoredPersonById([FromRoute] Guid id)
        {
            var callerId = GetCallerId();
            if (callerId == null)
                return Unauthorized(new { Message = ResponseMessages.InvalidToken });

            var monitoredPerson = await _monitoredService.GetMonitoredPersonByIdAsync(id);
            if (monitoredPerson == null)
                return NotFound(new { Message = ResponseMessages.MonitoredPersonNotFound });

            if (!IsAdminRole() && !await _userMonitoredService.UserOwnsMonitoredAsync(callerId.Value, id))
                return Forbid();

            return Ok(monitoredPerson);
        }

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

            if (dto.DeviceSerialNumber != existing.DeviceSerialNumber)
            {
                var conflict = await _monitoredService.GetMonitoredPersonByDeviceSerialNumberAsync(dto.DeviceSerialNumber);
                if (conflict != null && conflict.Id != id)
                    return Conflict(new { Message = "A monitored person with this device serial number already exists." });
            }

            existing.FirstName = dto.FirstName;
            existing.LastName = dto.LastName;
            existing.Birthdate = dto.Birthdate;
            existing.Gender = dto.Gender;
            existing.Address = dto.Address;
            existing.DeviceSerialNumber = dto.DeviceSerialNumber;
            existing.MinHeartRate = dto.MinHeartRate;
            existing.MaxHeartRate = dto.MaxHeartRate;
            existing.MinTemperature = dto.MinTemperature;
            existing.MaxTemperature = dto.MaxTemperature;
            existing.MinSpO2 = dto.MinSpO2;
            existing.MaxSpO2 = dto.MaxSpO2;
            existing.UpdateFrequency = dto.UpdateFrequency;
            existing.DataRetentionDays = dto.DataRetentionDays;
            existing.ArchiveRetentionDays = dto.ArchiveRetentionDays;
            existing.UpdatedAt = DateTime.UtcNow;

            await _monitoredService.UpdateMonitoredPersonAsync(existing);

            return Ok(new { Message = "Monitored person updated successfully.", MonitoredPerson = existing });
        }

        // ── Archive / Restore / Permanent delete ────────────────────────────────

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
                return Ok(new { Message = "Monitored person is already archived.", MonitoredPerson = existing });

            var ok = await _monitoredService.ArchiveMonitoredPersonAsync(id);
            if (!ok)
                return StatusCode(500, new { Message = "Failed to archive monitored person." });

            _alertMonitorService.InvalidateArchivedCache(id);
            _logger.LogInformation("User {UserId} archived monitored {MonitoredId}", callerId, id);
            _auditService.LogAsync(User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? callerId!.Value.ToString(), "ArchivePatient", $"Archived patient {existing.FirstName} {existing.LastName} (id={id})", "Patient");
            return Ok(new { Message = "Monitored person archived successfully." });
        }

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
                return Ok(new { Message = "Monitored person is not archived.", MonitoredPerson = existing });

            var ok = await _monitoredService.RestoreMonitoredPersonAsync(id);
            if (!ok)
                return StatusCode(500, new { Message = "Failed to restore monitored person." });

            _alertMonitorService.InvalidateArchivedCache(id);
            _logger.LogInformation("User {UserId} restored monitored {MonitoredId} from archive", callerId, id);
            _auditService.LogAsync(User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? callerId!.Value.ToString(), "RestorePatient", $"Restored patient {existing.FirstName} {existing.LastName} (id={id}) from archive", "Patient");
            return Ok(new { Message = "Monitored person restored successfully." });
        }

        /// <summary>
        /// Smart remove: if caller is the last owner, soft-deletes the monitored person
        /// (DeletedAt set; retention job hard-deletes after 7 days).
        /// If other owners exist, only removes the caller's UserMonitored link.
        /// Admin always soft-deletes regardless of owner count.
        /// </summary>
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
                var ok = await _monitoredService.SoftDeleteMonitoredPersonAsync(id);
                if (!ok) return StatusCode(500, new { Message = "Failed to delete monitored person." });
                _alertMonitorService.InvalidateArchivedCache(id);
                _logger.LogWarning("Admin {UserId} soft-deleted monitored {MonitoredId}", callerId, id);
                _auditService.LogAsync(User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? callerId!.Value.ToString(),
                    "SoftDeletePatient", $"Admin soft-deleted patient {existing.FirstName} {existing.LastName} (id={id})", "Patient");
                return Ok(new { wasLastOwner = true, message = "Persoana monitorizată a fost marcată pentru ștergere și va fi eliminată definitiv după 7 zile." });
            }

            var ownerCount = await _userMonitoredService.CountUsersForMonitoredAsync(id);
            if (ownerCount > 1)
            {
                await _userMonitoredService.RemoveUserMonitoredLinkAsync(callerId.Value, id);
                _logger.LogInformation("User {UserId} unlinked from monitored {MonitoredId} ({Count} owners remain)", callerId, id, ownerCount - 1);
                _auditService.LogAsync(User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? callerId!.Value.ToString(),
                    "UnlinkPatient", $"User unlinked from patient {existing.FirstName} {existing.LastName} (id={id})", "Patient");
                return Ok(new { wasLastOwner = false, message = "Ai fost deconectat de la această persoană monitorizată." });
            }
            else
            {
                var ok = await _monitoredService.SoftDeleteMonitoredPersonAsync(id);
                if (!ok) return StatusCode(500, new { Message = "Failed to delete monitored person." });
                _alertMonitorService.InvalidateArchivedCache(id);
                _logger.LogWarning("User {UserId} soft-deleted monitored {MonitoredId} (was last owner)", callerId, id);
                _auditService.LogAsync(User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? callerId!.Value.ToString(),
                    "SoftDeletePatient", $"Soft-deleted patient {existing.FirstName} {existing.LastName} (id={id})", "Patient");
                return Ok(new { wasLastOwner = true, message = "Persoana monitorizată a fost marcată pentru ștergere și va fi eliminată definitiv după 7 zile." });
            }
        }

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
                return BadRequest(new { Message = "Persoana monitorizată nu este marcată pentru ștergere." });

            var ok = await _monitoredService.ReactivateMonitoredPersonAsync(id);
            if (!ok) return StatusCode(500, new { Message = "Failed to reactivate monitored person." });

            _alertMonitorService.InvalidateArchivedCache(id);
            _logger.LogInformation("Admin {UserId} reactivated monitored {MonitoredId}", callerId, id);
            _auditService.LogAsync(User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? callerId!.Value.ToString(),
                "ReactivatePatient", $"Reactivated patient {existing.FirstName} {existing.LastName} (id={id})", "Patient");
            return Ok(new { Message = "Persoana monitorizată a fost reactivată." });
        }

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

            // Permanent delete is only allowed from the archive — guard against accidental
            // deletes of actively-monitored people.
            if (!existing.IsArchived)
                return BadRequest(new { Message = "Person must be archived before permanent deletion." });

            await _monitoredService.DeleteMonitoredPersonAsync(id);
            _logger.LogWarning("User {UserId} permanently deleted monitored {MonitoredId}", callerId, id);
            _auditService.LogAsync(User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? callerId!.Value.ToString(), "DeletePatient", $"Permanently deleted patient {existing.FirstName} {existing.LastName} (id={id})", "Patient");
            return Ok(new { Message = "Monitored person permanently deleted." });
        }
    }
}