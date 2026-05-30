using System.Linq;
using System.Security.Claims;
using LifeAlertPlus.API.Helpers;
using LifeAlertPlus.API.Services;
using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Infrastructure.Context;
using LifeAlertPlus.Shared.DTOs.Requests.Email;
using LifeAlertPlus.Shared.DTOs.Responses.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class InvitationsController : ControllerBase
    {
        private readonly IInvitationRepository _invitationRepository;
        private readonly IUserMonitoredService _userMonitoredService;
        private readonly IMonitoredService _monitoredService;
        private readonly IMeasurementService _measurementService;
        private readonly LifeAlertPlusDbContext _db;
        private readonly ILogger<InvitationsController> _logger;
        private readonly IPushNotificationService _pushNotificationService;
        private readonly IEmailService _emailService;

        public InvitationsController(
            IInvitationRepository invitationRepository,
            IUserMonitoredService userMonitoredService,
            IMonitoredService monitoredService,
            IMeasurementService measurementService,
            LifeAlertPlusDbContext db,
            ILogger<InvitationsController> logger,
            IPushNotificationService pushNotificationService,
            IEmailService emailService)
        {
            _invitationRepository = invitationRepository;
            _userMonitoredService = userMonitoredService;
            _monitoredService = monitoredService;
            _measurementService = measurementService;
            _db = db;
            _logger = logger;
            _pushNotificationService = pushNotificationService;
            _emailService = emailService;
        }

        [AllowAnonymous]
        [HttpGet("info")]
        public async Task<IActionResult> GetInvitationInfo([FromQuery] string token)
        {
            var invitation = await GetInvitationOrNullAsync(token);
            if (invitation == null)
                return NotFound(new { Message = ResponseMessages.InvitationNotFound });

            var monitored = await _monitoredService.GetMonitoredPersonByIdAsync(invitation.PatientId);
            var patientName = monitored != null ? $"{monitored.FirstName} {monitored.LastName}".Trim() : string.Empty;

            // Find the caregiver (first user who owns this patient).
            var caregiverEmail = await _db.UserMonitoreds
                .Where(um => um.IdMonitored == invitation.PatientId)
                .Join(_db.Users, um => um.IdUser, u => u.Id, (um, u) => u.Email)
                .FirstOrDefaultAsync() ?? string.Empty;

            var response = new InvitationInfoResponseDTO
            {
                DoctorEmail    = invitation.DoctorEmail,
                CaregiverEmail = caregiverEmail,
                PatientId      = invitation.PatientId,
                PatientName    = patientName,
                ExpiresAt      = invitation.ExpiresAt,
                IsAccepted     = invitation.IsAccepted,
                IsExpired      = invitation.ExpiresAt < DateTime.UtcNow
            };

            return Ok(response);
        }

        // Token-only access (no account required)
        [AllowAnonymous]
        [HttpGet("patient")]
        public async Task<IActionResult> GetPatientByToken([FromQuery] string token)
        {
            var invitation = await GetValidInvitationOrNullAsync(token);
            if (invitation == null)
                return NotFound(new { Message = "Invitation not found or expired." });

            var monitored = await _monitoredService.GetMonitoredPersonByIdAsync(invitation.PatientId);
            if (monitored == null)
                return NotFound(new { Message = ResponseMessages.MonitoredPersonNotFound });

            return Ok(monitored);
        }

        [AllowAnonymous]
        [HttpGet("measurements")]
        public async Task<IActionResult> GetPatientMeasurementsByToken(
            [FromQuery] string token,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 50)
        {
            var invitation = await GetValidInvitationOrNullAsync(token);
            if (invitation == null)
                return NotFound(new { Message = "Invitation not found or expired." });

            // clamp paging to avoid abuse
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var measurements = await _measurementService.GetMeasurementsByMonitoredIdAsync(invitation.PatientId, pageNumber, pageSize);
            return Ok(measurements ?? Enumerable.Empty<LifeAlertPlus.Shared.DTOs.Responses.Measurement.MeasurementResponseDTO>());
        }

        [Authorize]
        [HttpPost("accept")]
        public async Task<IActionResult> AcceptInvitation([FromBody] AcceptInvitationRequestDTO request)
        {
            if (string.IsNullOrWhiteSpace(request.Token))
                return BadRequest(new { Message = "Token is required." });

            var callerIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub")
                ?? User.FindFirstValue("nameid");

            if (callerIdStr == null || !Guid.TryParse(callerIdStr, out var callerId))
                return Unauthorized(new { Message = ResponseMessages.InvalidToken });

            var callerEmail = User.FindFirstValue(ClaimTypes.Email)
                ?? User.FindFirstValue("email")
                ?? string.Empty;

            if (string.IsNullOrWhiteSpace(callerEmail))
                return Unauthorized(new { Message = "Unable to determine authenticated email." });

            var invitation = await _invitationRepository.GetByTokenAsync(TokenHashHelper.ComputeSha256(request.Token));
            if (invitation == null)
                return NotFound(new { Message = ResponseMessages.InvitationNotFound });

            if (invitation.IsAccepted)
                return BadRequest(new { Message = "Invitation already accepted." });

            if (invitation.ExpiresAt < DateTime.UtcNow)
                return BadRequest(new { Message = "Invitation expired." });

            if (!invitation.DoctorEmail.Equals(callerEmail, StringComparison.OrdinalIgnoreCase))
                return Forbid();

            try
            {
                // Ensure patient still exists
                var monitored = await _monitoredService.GetMonitoredPersonByIdAsync(invitation.PatientId);
                if (monitored == null)
                    return NotFound(new { Message = ResponseMessages.MonitoredPersonNotFound });

                // Grant access
                await _userMonitoredService.AddMonitoredPersonToUserAsync(callerId, invitation.PatientId);

                // Mark invitation as accepted
                invitation.IsAccepted = true;
                await _invitationRepository.UpdateAsync(invitation);

                return Ok(new { Message = "Invitation accepted." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to accept invitation for {DoctorEmail} to patient {PatientId}", invitation.DoctorEmail, invitation.PatientId);
                return StatusCode(500, new { Message = "Failed to accept invitation." });
            }
        }

        // Doctor notes via invitation token (no JWT required — doctor is identified
        // by the invitation token itself, which carries their email).
        [AllowAnonymous]
        [HttpGet("notes")]
        public async Task<IActionResult> GetNotes([FromQuery] string token)
        {
            var inv = await GetValidInvitationOrNullAsync(token);
            if (inv == null) return Unauthorized(new { Message = "Invalid or expired token." });

            var notes = await _db.DoctorNotes
                .Where(n => n.IdMonitored == inv.PatientId)
                .OrderByDescending(n => n.CreatedAt)
                .Select(n => new { n.Id, n.DoctorEmail, n.DoctorName, n.Content, n.CreatedAt, n.UpdatedAt })
                .ToListAsync();

            return Ok(notes);
        }

        [AllowAnonymous]
        [HttpPost("notes")]
        public async Task<IActionResult> SaveNote([FromQuery] string token, [FromBody] InviteNoteRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Content))
                return BadRequest(new { Message = "Note content is required." });

            var inv = await GetValidInvitationOrNullAsync(token);
            if (inv == null) return Unauthorized(new { Message = "Invalid or expired token." });

            // Resolve doctor's display name from the User table if they registered.
            var doctorUser = await _db.Users
                .Where(u => u.Email == inv.DoctorEmail && u.DeletedAt == null)
                .Select(u => new { u.FirstName, u.LastName })
                .FirstOrDefaultAsync();
            var doctorName = doctorUser != null
                ? $"{doctorUser.FirstName} {doctorUser.LastName}".Trim()
                : inv.DoctorEmail;

            // Upsert: one note per doctor per patient.
            var existing = await _db.DoctorNotes
                .FirstOrDefaultAsync(n => n.IdMonitored == inv.PatientId
                    && n.DoctorEmail == inv.DoctorEmail);

            if (existing != null)
            {
                existing.Content   = request.Content.Trim();
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                _db.DoctorNotes.Add(new DoctorNote
                {
                    Id          = Guid.NewGuid(),
                    IdMonitored = inv.PatientId,
                    IdDoctor    = Guid.Empty, // doctor may not have an account yet
                    DoctorEmail = inv.DoctorEmail,
                    DoctorName  = doctorName,
                    Content     = request.Content.Trim(),
                    CreatedAt   = DateTime.UtcNow
                });
            }

            await _db.SaveChangesAsync();

            // Notify all caregivers owning this patient.
            var monitored = await _db.Monitoreds.FindAsync(inv.PatientId);
            var patientName = monitored != null ? $"{monitored.FirstName} {monitored.LastName}".Trim() : "patient";
            var caregivers = await _db.UserMonitoreds
                .Where(um => um.IdMonitored == inv.PatientId)
                .Include(um => um.User)
                .ToListAsync();

            foreach (var um in caregivers)
            {
                var u = um.User;
                if (u == null || u.DeletedAt.HasValue) continue;
                var isEn = string.Equals(u.Language, "en", StringComparison.OrdinalIgnoreCase);
                var msg = isEn
                    ? $"{inv.DoctorEmail} added/updated a note for {patientName}."
                    : $"{inv.DoctorEmail} a adăugat/actualizat o notiță pentru {patientName}.";

                _db.Notifications.Add(new Notification
                {
                    Id = Guid.NewGuid(),
                    IdUser = u.Id,
                    IdMonitored = inv.PatientId,
                    NotificationType = "Info",
                    Message = msg,
                    CreatedAt = DateTime.UtcNow
                });

                if (u.NotifyByPush)
                    try { await _pushNotificationService.SendPushNotificationAsync(u.Id, $"📝 {msg}", "Info"); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Doctor note push failed for user {UserId}", u.Id); }

                if (u.NotifyByEmail)
                    try
                    {
                        var notePreview = request.Content.Trim().Length > 200
                            ? request.Content.Trim()[..197] + "..."
                            : request.Content.Trim();
                        await _emailService.SendDoctorNoteNotificationEmailAsync(
                            u.Email,
                            $"{u.FirstName} {u.LastName}".Trim(),
                            patientName,
                            inv.DoctorEmail,
                            notePreview,
                            isEn ? "en" : "ro");
                    }
                    catch (Exception ex) { _logger.LogWarning(ex, "Doctor note email failed for user {UserId}", u.Id); }
            }

            await _db.SaveChangesAsync();
            _logger.LogInformation("Doctor {DoctorEmail} saved note for patient {PatientId}", inv.DoctorEmail, inv.PatientId);
            return Ok(new { Message = "Note saved." });
        }

        public record InviteNoteRequest(string Content);

        private async Task<Invitation?> GetInvitationOrNullAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return null;

            return await _invitationRepository.GetByTokenAsync(TokenHashHelper.ComputeSha256(token));
        }

        private async Task<Invitation?> GetValidInvitationOrNullAsync(string token)
        {
            var invitation = await GetInvitationOrNullAsync(token);
            if (invitation == null)
                return null;

            if (invitation.ExpiresAt < DateTime.UtcNow)
                return null;

            return invitation;
        }

    }
}
