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
    // Controller pentru sistemul de invitații medici.
    // Un îngrijitor poate invita un medic să vadă datele pacientului fără a-i cere contul propriu.
    // Fluxul: îngrijitor trimite email cu token → medicul accesează date via token → poate accepta invitația
    // (dacă are cont) și poate adăuga notițe medicale pentru pacient.
    // IMPORTANT: Majoritatea endpoint-urilor sunt [AllowAnonymous] — securitatea se face prin token-ul din URL.
    [ApiController]
    [Route("api/[controller]")]
    public class InvitationsController : ControllerBase
    {
        private readonly IInvitationRepository _invitationRepository;     // Acces DB invitații
        private readonly IUserMonitoredService _userMonitoredService;     // Acordare acces la pacient
        private readonly IMonitoredService _monitoredService;             // Date pacient
        private readonly IMeasurementService _measurementService;         // Măsurători pacient
        private readonly LifeAlertPlusDbContext _db;                      // Acces direct DB (EF Core)
        private readonly ILogger<InvitationsController> _logger;
        private readonly IPushNotificationService _pushNotificationService; // Push notification îngrijitor
        private readonly IEmailService _emailService;                     // Email îngrijitor

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
            _invitationRepository     = invitationRepository;
            _userMonitoredService     = userMonitoredService;
            _monitoredService         = monitoredService;
            _measurementService       = measurementService;
            _db                       = db;
            _logger                   = logger;
            _pushNotificationService  = pushNotificationService;
            _emailService             = emailService;
        }

        // GET /api/invitations/info?token=... — Informații despre invitație (fără a accepta)
        // Folosit de pagina de preview a invitației pentru a afișa detalii înainte de acceptare
        [AllowAnonymous] // Tokenul din URL este suficient pentru autentificare
        [HttpGet("info")]
        public async Task<IActionResult> GetInvitationInfo([FromQuery] string token)
        {
            // Căutăm invitația în DB după hash-ul token-ului (token-ul raw e în URL, hash-ul în DB)
            var invitation = await GetInvitationOrNullAsync(token);
            if (invitation == null)
                return NotFound(new { Message = ResponseMessages.InvitationNotFound });

            var monitored = await _monitoredService.GetMonitoredPersonByIdAsync(invitation.PatientId);
            var patientName = monitored != null ? $"{monitored.FirstName} {monitored.LastName}".Trim() : string.Empty;

            // Găsim emailul primului îngrijitor care are acest pacient — afișat medicului pentru context
            var caregiverEmail = await _db.UserMonitoreds
                .Where(um => um.IdMonitored == invitation.PatientId)
                .Join(_db.Users, um => um.IdUser, u => u.Id, (um, u) => u.Email)
                .FirstOrDefaultAsync() ?? string.Empty;

            var response = new InvitationInfoResponseDTO
            {
                DoctorEmail    = invitation.DoctorEmail,   // Emailul medicului invitat
                CaregiverEmail = caregiverEmail,            // Emailul îngrijitorului care a trimis invitația
                PatientId      = invitation.PatientId,
                PatientName    = patientName,
                ExpiresAt      = invitation.ExpiresAt,
                IsAccepted     = invitation.IsAccepted,
                IsExpired      = invitation.ExpiresAt < DateTime.UtcNow // Calculat la momentul cererii
            };

            return Ok(response);
        }

        // GET /api/invitations/patient?token=... — Datele complete ale pacientului, accesibile doar cu token valid
        // Medicul poate vedea profilul pacientului fără cont propriu
        [AllowAnonymous]
        [HttpGet("patient")]
        public async Task<IActionResult> GetPatientByToken([FromQuery] string token)
        {
            // Validăm token-ul ȘI verificăm că nu e expirat
            var invitation = await GetValidInvitationOrNullAsync(token);
            if (invitation == null)
                return NotFound(new { Message = "Invitation not found or expired." });

            var monitored = await _monitoredService.GetMonitoredPersonByIdAsync(invitation.PatientId);
            if (monitored == null)
                return NotFound(new { Message = ResponseMessages.MonitoredPersonNotFound });

            return Ok(monitored);
        }

        // GET /api/invitations/measurements?token=...&pageNumber=1&pageSize=50 — Măsurătorile pacientului
        // Medicul poate vedea istoricul de sănătate al pacientului prin token (fără cont)
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

            // Limităm paginarea pentru a preveni abuzuri (maxim 200 pe pagină)
            pageNumber = Math.Max(1, pageNumber);
            pageSize   = Math.Clamp(pageSize, 1, 200);

            var measurements = await _measurementService.GetMeasurementsByMonitoredIdAsync(invitation.PatientId, pageNumber, pageSize);
            return Ok(measurements ?? Enumerable.Empty<LifeAlertPlus.Shared.DTOs.Responses.Measurement.MeasurementResponseDTO>());
        }

        // POST /api/invitations/accept — Medicul acceptă invitația și primește acces permanent la pacient
        // Necesită JWT (medicul trebuie să aibă cont) — emailul JWT trebuie să coincidă cu emailul din invitație
        [Authorize]
        [HttpPost("accept")]
        public async Task<IActionResult> AcceptInvitation([FromBody] AcceptInvitationRequestDTO request)
        {
            if (string.IsNullOrWhiteSpace(request.Token))
                return BadRequest(new { Message = "Token is required." });

            // Extragem ID-ul și emailul din JWT (claims standard)
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

            // Căutăm invitația după hash-ul SHA-256 al token-ului (nu token-ul raw)
            var invitation = await _invitationRepository.GetByTokenAsync(TokenHashHelper.ComputeSha256(request.Token));
            if (invitation == null)
                return NotFound(new { Message = ResponseMessages.InvitationNotFound });

            if (invitation.IsAccepted)
                return BadRequest(new { Message = "Invitation already accepted." }); // Invitație deja folosită

            if (invitation.ExpiresAt < DateTime.UtcNow)
                return BadRequest(new { Message = "Invitation expired." }); // Invitație expirată (24h)

            // Verificăm că emailul din JWT coincide cu emailul medicului invitat (securitate)
            if (!invitation.DoctorEmail.Equals(callerEmail, StringComparison.OrdinalIgnoreCase))
                return Forbid(); // Alt utilizator încearcă să folosească invitația

            try
            {
                var monitored = await _monitoredService.GetMonitoredPersonByIdAsync(invitation.PatientId);
                if (monitored == null)
                    return NotFound(new { Message = ResponseMessages.MonitoredPersonNotFound });

                // Adăugăm relația User-Monitored: medicul capătă acces la pacient
                await _userMonitoredService.AddMonitoredPersonToUserAsync(callerId, invitation.PatientId);

                // Marcăm invitația ca acceptată (nu poate fi reutilizată)
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

        // GET /api/invitations/notes?token=... — Toate notițele medicale pentru pacientul din invitație
        // Accesibil fără cont — medicul e identificat prin token-ul invitației
        [AllowAnonymous]
        [HttpGet("notes")]
        public async Task<IActionResult> GetNotes([FromQuery] string token)
        {
            var inv = await GetValidInvitationOrNullAsync(token);
            if (inv == null) return Unauthorized(new { Message = "Invalid or expired token." });

            // Returnăm toate notițele medicale ale pacientului (de la toți medicii)
            var notes = await _db.DoctorNotes
                .Where(n => n.IdMonitored == inv.PatientId)
                .OrderByDescending(n => n.CreatedAt)
                .Select(n => new { n.Id, n.DoctorEmail, n.DoctorName, n.Content, n.CreatedAt, n.UpdatedAt })
                .ToListAsync();

            return Ok(notes);
        }

        // POST /api/invitations/notes?token=... — Salvează o notiță medicală (upsert: o notiță per medic per pacient)
        // Dacă medicul a mai salvat o notiță, o actualizează; altfel creează una nouă
        // Notifică toți îngrijitorii pacientului că a fost adăugată o notiță nouă
        [AllowAnonymous]
        [HttpPost("notes")]
        public async Task<IActionResult> SaveNote([FromQuery] string token, [FromBody] InviteNoteRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Content))
                return BadRequest(new { Message = "Note content is required." });

            var inv = await GetValidInvitationOrNullAsync(token);
            if (inv == null) return Unauthorized(new { Message = "Invalid or expired token." });

            // Încercăm să găsim numele medicului în tabela Users (dacă are cont înregistrat)
            var doctorUser = await _db.Users
                .Where(u => u.Email == inv.DoctorEmail && u.DeletedAt == null)
                .Select(u => new { u.FirstName, u.LastName })
                .FirstOrDefaultAsync();
            var doctorName = doctorUser != null
                ? $"{doctorUser.FirstName} {doctorUser.LastName}".Trim()
                : inv.DoctorEmail; // Fallback: emailul ca nume dacă nu are cont

            // UPSERT: verificăm dacă există deja o notiță de la acest medic pentru acest pacient
            var existing = await _db.DoctorNotes
                .FirstOrDefaultAsync(n => n.IdMonitored == inv.PatientId
                    && n.DoctorEmail == inv.DoctorEmail);

            if (existing != null)
            {
                // Actualizăm notița existentă (înlocuim complet conținutul)
                existing.Content   = request.Content.Trim();
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                // Creăm o notiță nouă
                _db.DoctorNotes.Add(new DoctorNote
                {
                    Id          = Guid.NewGuid(),
                    IdMonitored = inv.PatientId,
                    IdDoctor    = Guid.Empty,        // Medicul poate să nu aibă cont înregistrat
                    DoctorEmail = inv.DoctorEmail,
                    DoctorName  = doctorName,
                    Content     = request.Content.Trim(),
                    CreatedAt   = DateTime.UtcNow
                });
            }

            await _db.SaveChangesAsync(); // Salvăm notița înainte de a trimite notificările

            // Notificăm toți îngrijitorii care monitorizează acest pacient
            var monitored  = await _db.Monitoreds.FindAsync(inv.PatientId);
            var patientName = monitored != null ? $"{monitored.FirstName} {monitored.LastName}".Trim() : "patient";
            var caregivers  = await _db.UserMonitoreds
                .Where(um => um.IdMonitored == inv.PatientId)
                .Include(um => um.User) // Încărcăm și datele utilizatorului
                .ToListAsync();

            foreach (var um in caregivers)
            {
                var u = um.User;
                if (u == null || u.DeletedAt.HasValue) continue; // Sărim utilizatorii șterși

                var isEn = string.Equals(u.Language, "en", StringComparison.OrdinalIgnoreCase);
                var msg  = isEn
                    ? $"{inv.DoctorEmail} added/updated a note for {patientName}."
                    : $"{inv.DoctorEmail} a adăugat/actualizat o notiță pentru {patientName}.";

                // Adăugăm notificarea în DB (apare în inbox-ul utilizatorului)
                _db.Notifications.Add(new Notification
                {
                    Id               = Guid.NewGuid(),
                    IdUser           = u.Id,
                    IdMonitored      = inv.PatientId,
                    NotificationType = "Info",
                    Message          = msg,
                    CreatedAt        = DateTime.UtcNow
                });

                // Trimitere push notification (dacă e activat)
                if (u.NotifyByPush)
                    try { await _pushNotificationService.SendPushNotificationAsync(u.Id, $"📝 {msg}", "Info"); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Doctor note push failed for user {UserId}", u.Id); }

                // Trimitere email cu preview notiță (dacă e activat)
                if (u.NotifyByEmail)
                    try
                    {
                        // Limităm preview-ul la 200 caractere pentru email
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

            await _db.SaveChangesAsync(); // Salvăm notificările
            _logger.LogInformation("Doctor {DoctorEmail} saved note for patient {PatientId}", inv.DoctorEmail, inv.PatientId);
            return Ok(new { Message = "Note saved." });
        }

        // DTO simplu pentru conținutul notițelor (record — imutabil și concis)
        public record InviteNoteRequest(string Content);

        // Helper: găsește invitația după token raw (fără validare expirare)
        private async Task<Invitation?> GetInvitationOrNullAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return null;

            // Calculăm hash-ul SHA-256 pentru a compara cu hash-ul din DB
            return await _invitationRepository.GetByTokenAsync(TokenHashHelper.ComputeSha256(token));
        }

        // Helper: găsește invitația și verifică că NU a expirat (folosit pentru endpoint-uri de acces date)
        private async Task<Invitation?> GetValidInvitationOrNullAsync(string token)
        {
            var invitation = await GetInvitationOrNullAsync(token);
            if (invitation == null)
                return null;

            if (invitation.ExpiresAt < DateTime.UtcNow)
                return null; // Token-ul a expirat

            return invitation;
        }
    }
}
