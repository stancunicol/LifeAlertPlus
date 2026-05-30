using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.API.Services;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Infrastructure.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/monitored/{monitoredId:guid}/notes")]
    public class DoctorNoteController(
        LifeAlertPlusDbContext db,
        IUserMonitoredService userMonitoredService,
        IPushNotificationService pushNotificationService,
        IEmailService emailService,
        ILogger<DoctorNoteController> logger) : BaseApiController
    {
        // Any user who can view the monitored person (owner or invited doctor) can read notes.
        private async Task<bool> HasViewAccessAsync(Guid monitoredId)
        {
            var callerId = GetCallerId();
            if (callerId == null) return false;
            if (IsAdminRole()) return true;
            var owned = await userMonitoredService.GetMonitoredPeopleByUserIdAsync(callerId.Value);
            if (owned.Any(m => m.Id == monitoredId)) return true;
            // Also allow doctors who accepted an invitation for this patient.
            return await db.Invitations.AnyAsync(i =>
                i.PatientId == monitoredId &&
                i.IsAccepted &&
                string.Equals(i.DoctorEmail, User.FindFirst(System.Security.Claims.ClaimTypes.Email)!.Value, StringComparison.OrdinalIgnoreCase));
        }

        [HttpGet]
        public async Task<IActionResult> GetNotes(Guid monitoredId)
        {
            if (!await HasViewAccessAsync(monitoredId)) return Forbid();
            var notes = await db.DoctorNotes
                .Where(n => n.IdMonitored == monitoredId)
                .OrderByDescending(n => n.CreatedAt)
                .Select(n => new { n.Id, n.DoctorEmail, n.DoctorName, n.Content, n.CreatedAt, n.UpdatedAt })
                .ToListAsync();
            return Ok(notes);
        }

        [HttpPost]
        public async Task<IActionResult> SaveNote(Guid monitoredId, [FromBody] DoctorNoteRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Content))
                return BadRequest(new { Message = "Note content is required." });

            var callerId = GetCallerId();
            if (callerId == null) return Unauthorized();

            // Only invited doctors can leave notes — not owners or admins.
            if (IsAdminRole())
                return Forbid();

            // Check if the caller is an invited doctor (not the owner)
            var isInvitedDoctor = await db.Invitations.AnyAsync(i =>
                i.PatientId == monitoredId &&
                i.IsAccepted &&
                string.Equals(i.DoctorEmail, User.FindFirst(System.Security.Claims.ClaimTypes.Email)!.Value, StringComparison.OrdinalIgnoreCase));

            if (!isInvitedDoctor) return Forbid("Only invited doctors can add medical notes.");

            var doctorUser = await db.Users.FindAsync(callerId.Value);
            if (doctorUser == null) return Unauthorized();

            // Upsert: one note per doctor per patient.
            var existing = await db.DoctorNotes
                .FirstOrDefaultAsync(n => n.IdMonitored == monitoredId && n.IdDoctor == callerId.Value);

            if (existing != null)
            {
                existing.Content = request.Content.Trim();
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                db.DoctorNotes.Add(new DoctorNote
                {
                    Id = Guid.NewGuid(),
                    IdMonitored = monitoredId,
                    IdDoctor = callerId.Value,
                    DoctorEmail = doctorUser.Email,
                    DoctorName = $"{doctorUser.FirstName} {doctorUser.LastName}".Trim(),
                    Content = request.Content.Trim(),
                    CreatedAt = DateTime.UtcNow
                });
            }
            await db.SaveChangesAsync();

            // Notification to all caregivers owning this patient.
            var monitored = await db.Monitoreds.FindAsync(monitoredId);
            var patientName = monitored != null ? $"{monitored.FirstName} {monitored.LastName}".Trim() : "patient";
            var caregivers = await db.UserMonitoreds
                .Where(um => um.IdMonitored == monitoredId)
                .Include(um => um.User)
                .ToListAsync();

            foreach (var um in caregivers)
            {
                var u = um.User;
                if (u == null || u.DeletedAt.HasValue || u.Id == callerId.Value) continue;
                var isEn = string.Equals(u.Language, "en", StringComparison.OrdinalIgnoreCase);
                var msg = isEn
                    ? $"Dr. {doctorUser.FirstName} {doctorUser.LastName} added/updated a note for {patientName}."
                    : $"Dr. {doctorUser.FirstName} {doctorUser.LastName} a adăugat/actualizat o notiță pentru {patientName}.";

                // Save notification to database
                db.Notifications.Add(new Notification
                {
                    Id = Guid.NewGuid(),
                    IdUser = u.Id,
                    IdMonitored = monitoredId,
                    NotificationType = "Info",
                    Message = msg,
                    CreatedAt = DateTime.UtcNow
                });

                // Send push notification if user has it enabled
                if (u.NotifyByPush)
                    try { await pushNotificationService.SendPushNotificationAsync(u.Id, $"📝 {msg}", "Info"); }
                    catch (Exception ex) { logger.LogWarning(ex, "Doctor note push failed for user {UserId}", u.Id); }

                // Send email notification if user has it enabled
                if (u.NotifyByEmail)
                {
                    try
                    {
                        var userName = $"{u.FirstName} {u.LastName}".Trim();
                        var notePreview = request.Content.Trim().Length > 200
                            ? request.Content.Trim().Substring(0, 197) + "..."
                            : request.Content.Trim();
                        await emailService.SendDoctorNoteNotificationEmailAsync(
                            u.Email,
                            userName,
                            patientName,
                            doctorUser.Email,
                            notePreview,
                            isEn ? "en" : "ro"
                        );
                    }
                    catch (Exception ex) { logger.LogWarning(ex, "Doctor note email failed for user {UserId}", u.Id); }
                }
            }

            await db.SaveChangesAsync();

            logger.LogInformation("Doctor {DoctorId} saved note for monitored {MonitoredId}", callerId, monitoredId);
            return Ok(new { Message = "Note saved." });
        }

        public record DoctorNoteRequest(string Content);
    }
}
