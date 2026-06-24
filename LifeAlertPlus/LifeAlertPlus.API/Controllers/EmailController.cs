using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using LifeAlertPlus.API.Helpers;
using LifeAlertPlus.API.Services;
using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Shared.DTOs.Requests.Email;
using LifeAlertPlus.Shared.DTOs.Responses.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace LifeAlertPlus.API.Controllers
{
    // Controller pentru trimiterea emailurilor speciale: invitații medici și rapoarte PDF.
    // Emailurile normale de alertă sunt trimise automat de AlertMonitorService;
    // aceste endpoint-uri sunt acțiuni explicite ale utilizatorului.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // Necesită autentificare
    public class EmailController : BaseApiController
    {
        private readonly IEmailService _emailService;                   // Trimitere email via SMTP
        private readonly IInvitationRepository _invitationRepository;   // Stocare invitație în DB
        private readonly IUserMonitoredService _userMonitoredService;   // Verificare drept de acces
        private readonly IMonitoredService _monitoredService;           // Date pacient pentru email
        private readonly GetUrlService _urlService;                     // URL-ul clientului Blazor (pentru link-ul din email)
        private readonly ILogger<EmailController> _logger;

        public EmailController(
            IEmailService emailService,
            IInvitationRepository invitationRepository,
            IUserMonitoredService userMonitoredService,
            IMonitoredService monitoredService,
            GetUrlService urlService,
            ILogger<EmailController> logger)
        {
            _emailService = emailService;
            _invitationRepository = invitationRepository;
            _userMonitoredService = userMonitoredService;
            _monitoredService = monitoredService;
            _urlService = urlService;
            _logger = logger;
        }

        // POST /api/email/send-doctor-invitation — Trimite invitație unui medic să acceseze datele unui pacient
        // Generează un token securizat (SHA-256 hash stocat în DB), expirat în 24 ore
        // Medicul primește un link cu token-ul raw; la click, poate vedea datele fără cont propriu
        [HttpPost("send-doctor-invitation")]
        public async Task<IActionResult> SendDoctorInvitation([FromBody] SendDoctorInvitationRequestDTO request)
        {
            if (request.PatientId == Guid.Empty || string.IsNullOrWhiteSpace(request.PatientName) || string.IsNullOrWhiteSpace(request.DoctorEmail))
                return BadRequest(new { Message = "Doctor email, patient ID și numele pacientului sunt necesare." });

            // Extragem ID-ul utilizatorului din token-ul JWT (claims standard)
            var callerIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub")
                ?? User.FindFirstValue("nameid");

            if (callerIdStr == null || !Guid.TryParse(callerIdStr, out var callerId))
                return Unauthorized(new { Message = ResponseMessages.InvalidToken });

            var isAdmin = IsAdminRole();

            // Verificăm că utilizatorul are dreptul să trimită invitații pentru acest pacient
            if (!isAdmin)
            {
                var owned = await _userMonitoredService.GetMonitoredPeopleByUserIdAsync(callerId);
                if (!owned.Any(m => m.Id == request.PatientId))
                    return Forbid(); // 403 — nu este îngrijitorul acestui pacient
            }

            // Verificăm că pacientul există în DB
            var patient = await _monitoredService.GetMonitoredPersonByIdAsync(request.PatientId);
            if (patient == null)
                return NotFound(new { Message = ResponseMessages.MonitoredPersonNotFound });

            try
            {
                // Generăm un token aleatoriu de 32 bytes, encodat Base64URL (sigur pentru URL-uri)
                var rawToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
                // Stocăm hash-ul SHA-256 în DB — tokenul raw nu este niciodată persistent
                // (același principiu ca parolele: compromiterea DB nu expune tokenii)
                var tokenHash = TokenHashHelper.ComputeSha256(rawToken);

                var invitation = new Invitation
                {
                    Id          = Guid.NewGuid(),
                    DoctorEmail = request.DoctorEmail.Trim(),
                    PatientId   = request.PatientId,
                    Token       = tokenHash,                          // Hash stocat în DB
                    ExpiresAt   = DateTime.UtcNow.AddHours(24),     // Valabil 24 de ore
                    IsAccepted  = false,
                    CreatedAt   = DateTime.UtcNow
                };

                await _invitationRepository.AddAsync(invitation); // Salvăm invitația în DB

                // Construim link-ul de invitație cu tokenul RAW (nu hash-ul!)
                var clientBase = _urlService.GetClientBaseUrl();
                var invitationLink = $"{clientBase}/invite/patient?token={Uri.EscapeDataString(rawToken)}";

                var patientDisplayName = $"{patient.FirstName} {patient.LastName}".Trim();
                // Trimitem emailul cu link-ul de invitație medicului
                await _emailService.SendDoctorInvitationEmailAsync(invitation.DoctorEmail, patientDisplayName, invitationLink);

                // Returnăm confirmarea cu tokenul raw (pentru test/debug — clientul îl poate afișa)
                return Ok(new InvitationResponseDTO
                {
                    DoctorEmail = invitation.DoctorEmail,
                    PatientId   = invitation.PatientId,
                    Token       = rawToken,               // Tokenul raw pentru afișare în UI
                    ExpiresAt   = invitation.ExpiresAt,
                    IsAccepted  = invitation.IsAccepted
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send doctor invitation to {DoctorEmail} for patient {PatientId}", request.DoctorEmail, request.PatientId);
                return StatusCode(500, new { Message = "Failed to send invitation email. Please try again later." });
            }
        }

        // POST /api/email/send-report — Trimite un raport PDF al unui pacient la adresa unui medic
        // PDF-ul este generat de Blazor și trimis ca Base64 în corpul cererii
        [HttpPost("send-report")]
        public async Task<IActionResult> SendReport([FromBody] SendReportEmailRequestDTO request)
        {
            if (string.IsNullOrWhiteSpace(request.DoctorEmail) || string.IsNullOrWhiteSpace(request.PdfBase64))
                return BadRequest(new { Message = "Doctor email and PDF are required." });

            try
            {
                var pdfBytes = Convert.FromBase64String(request.PdfBase64); // Decodăm PDF-ul din Base64
                await _emailService.SendReportEmailAsync(request.DoctorEmail, request.PatientName, pdfBytes);
                return Ok(new { Message = "Report sent successfully." });
            }
            catch (FormatException)
            {
                return BadRequest(new { Message = "Invalid PDF data." }); // Base64 invalid
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send report email to {DoctorEmail}", request.DoctorEmail);
                return StatusCode(500, new { Message = "Failed to send report email. Please try again later." });
            }
        }
    }
}
