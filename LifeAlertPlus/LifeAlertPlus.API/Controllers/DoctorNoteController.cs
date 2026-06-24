using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.API.Services;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Infrastructure.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LifeAlertPlus.API.Controllers
{
    // Controller pentru notițele medicale ale doctorilor.
    // Notițele sunt scrise de medici invitați pentru pacienți specifici.
    // RESTRICȚIE: Numai medicii INVITAȚI (cu invitație acceptată) pot adăuga notițe.
    //             Îngrijitorii și adminii pot CITI notițele, dar nu pot adăuga.
    // Ruta: /api/monitored/{monitoredId}/notes — notițele sunt legate de persoana monitorizată
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
        // Verifică dreptul de CITIRE: îngrijitor, admin sau medic invitat cu invitație acceptată
        private async Task<bool> HasViewAccessAsync(Guid monitoredId)
        {
            var callerId = GetCallerId();
            if (callerId == null) return false;
            if (IsAdminRole()) return true;
            // Verificăm dacă utilizatorul este îngrijitor al pacientului
            var owned = await userMonitoredService.GetMonitoredPeopleByUserIdAsync(callerId.Value);
            if (owned.Any(m => m.Id == monitoredId)) return true;
            // Verificăm dacă este medic cu invitație acceptată pentru acest pacient
            return await db.Invitations.AnyAsync(i =>
                i.PatientId == monitoredId &&
                i.IsAccepted &&
                string.Equals(i.DoctorEmail, User.FindFirst(System.Security.Claims.ClaimTypes.Email)!.Value, StringComparison.OrdinalIgnoreCase));
        }

        // GET /api/monitored/{monitoredId}/notes — Lista notițelor medicale pentru pacient
        // Accesibil de îngrijitori, admini și medici invitați cu acces acceptat
        [HttpGet]
        public async Task<IActionResult> GetNotes(Guid monitoredId)
        {
            if (!await HasViewAccessAsync(monitoredId)) return Forbid();
            var notes = await db.DoctorNotes
                .Where(n => n.IdMonitored == monitoredId)
                .OrderByDescending(n => n.CreatedAt) // Cele mai recente primele
                .Select(n => new { n.Id, n.DoctorEmail, n.DoctorName, n.Content, n.CreatedAt, n.UpdatedAt })
                .ToListAsync();
            return Ok(notes);
        }

        // POST /api/monitored/{monitoredId}/notes — Salvează o notiță medicală (upsert)
        // RESTRICȚIE: Numai medicii cu invitație ACCEPTATĂ pot adăuga notițe (nu îngrijitorii/adminii)
        // UPSERT: Dacă medicul a mai lăsat o notiță, o înlocuiește (un medic = o notiță per pacient)
        // Notifică toți îngrijitorii că a fost adăugată o notiță nouă
        [HttpPost]
        public async Task<IActionResult> SaveNote(Guid monitoredId, [FromBody] DoctorNoteRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Content))
                return BadRequest(new { Message = "Note content is required." });

            var callerId = GetCallerId();
            if (callerId == null) return Unauthorized();

            // Adminii NU pot adăuga notițe medicale (conflict de rol)
            if (IsAdminRole())
                return Forbid();

            // Verificăm că utilizatorul curent este un medic cu invitație acceptată pentru acest pacient
            var isInvitedDoctor = await db.Invitations.AnyAsync(i =>
                i.PatientId == monitoredId &&
                i.IsAccepted && // Invitația trebuie să fi fost acceptată
                string.Equals(i.DoctorEmail, User.FindFirst(System.Security.Claims.ClaimTypes.Email)!.Value, StringComparison.OrdinalIgnoreCase));

            if (!isInvitedDoctor) return Forbid("Only invited doctors can add medical notes.");

            var doctorUser = await db.Users.FindAsync(callerId.Value);
            if (doctorUser == null) return Unauthorized();

            // UPSERT: verificăm dacă medicul a mai lăsat o notiță pentru acest pacient
            var existing = await db.DoctorNotes
                .FirstOrDefaultAsync(n => n.IdMonitored == monitoredId && n.IdDoctor == callerId.Value);

            if (existing != null)
            {
                // Actualizăm notița existentă (înlocuire completă de conținut)
                existing.Content   = request.Content.Trim();
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                // Creăm o notiță nouă pentru acest medic și pacient
                db.DoctorNotes.Add(new DoctorNote
                {
                    Id          = Guid.NewGuid(),
                    IdMonitored = monitoredId,
                    IdDoctor    = callerId.Value,   // ID-ul medicului (utilizator înregistrat)
                    DoctorEmail = doctorUser.Email,
                    DoctorName  = $"{doctorUser.FirstName} {doctorUser.LastName}".Trim(),
                    Content     = request.Content.Trim(),
                    CreatedAt   = DateTime.UtcNow
                });
            }
            await db.SaveChangesAsync(); // Salvăm notița mai întâi

            // Notificăm toți îngrijitorii pacientului că a apărut o notiță nouă
            var monitored   = await db.Monitoreds.FindAsync(monitoredId);
            var patientName  = monitored != null ? $"{monitored.FirstName} {monitored.LastName}".Trim() : "patient";
            var caregivers   = await db.UserMonitoreds
                .Where(um => um.IdMonitored == monitoredId)
                .Include(um => um.User)
                .ToListAsync();

            foreach (var um in caregivers)
            {
                var u = um.User;
                // Sărim utilizatorii șterși și medicul însuși (nu se notifică pe sine)
                if (u == null || u.DeletedAt.HasValue || u.Id == callerId.Value) continue;
                var isEn = string.Equals(u.Language, "en", StringComparison.OrdinalIgnoreCase);
                var msg  = isEn
                    ? $"Dr. {doctorUser.FirstName} {doctorUser.LastName} added/updated a note for {patientName}."
                    : $"Dr. {doctorUser.FirstName} {doctorUser.LastName} a adăugat/actualizat o notiță pentru {patientName}.";

                // Adăugăm notificarea în DB (apare în inbox-ul îngrijitorului)
                db.Notifications.Add(new Notification
                {
                    Id               = Guid.NewGuid(),
                    IdUser           = u.Id,
                    IdMonitored      = monitoredId,
                    NotificationType = "Info",
                    Message          = msg,
                    CreatedAt        = DateTime.UtcNow
                });

                // Trimitere push notification (dacă e activat)
                if (u.NotifyByPush)
                    try { await pushNotificationService.SendPushNotificationAsync(u.Id, $"📝 {msg}", "Info"); }
                    catch (Exception ex) { logger.LogWarning(ex, "Doctor note push failed for user {UserId}", u.Id); }

                // Trimitere email cu preview notiță (dacă e activat)
                if (u.NotifyByEmail)
                {
                    try
                    {
                        var userName    = $"{u.FirstName} {u.LastName}".Trim();
                        // Preview-ul emailului este limitat la 200 caractere
                        var notePreview = request.Content.Trim().Length > 200
                            ? request.Content.Trim().Substring(0, 197) + "..."
                            : request.Content.Trim();
                        await emailService.SendDoctorNoteNotificationEmailAsync(
                            u.Email, userName, patientName, doctorUser.Email, notePreview,
                            isEn ? "en" : "ro");
                    }
                    catch (Exception ex) { logger.LogWarning(ex, "Doctor note email failed for user {UserId}", u.Id); }
                }
            }

            await db.SaveChangesAsync(); // Salvăm notificările

            logger.LogInformation("Doctor {DoctorId} saved note for monitored {MonitoredId}", callerId, monitoredId);
            return Ok(new { Message = "Note saved." });
        }

        // DTO simplu pentru conținutul notițelor (record — imutabil)
        public record DoctorNoteRequest(string Content);
    }
}
