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
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class EmailController : BaseApiController
    {
        private readonly IEmailService _emailService;
        private readonly IInvitationRepository _invitationRepository;
        private readonly IUserMonitoredService _userMonitoredService;
        private readonly IMonitoredService _monitoredService;
        private readonly GetUrlService _urlService;
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

        [HttpPost("send-doctor-invitation")]
        public async Task<IActionResult> SendDoctorInvitation([FromBody] SendDoctorInvitationRequestDTO request)
        {
            if (request.PatientId == Guid.Empty || string.IsNullOrWhiteSpace(request.PatientName) || string.IsNullOrWhiteSpace(request.DoctorEmail))
                return BadRequest(new { Message = "Doctor email, patient ID și numele pacientului sunt necesare." });

            // Authorization: user must own that monitored person, unless admin.
            var callerIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub")
                ?? User.FindFirstValue("nameid");

            if (callerIdStr == null || !Guid.TryParse(callerIdStr, out var callerId))
                return Unauthorized(new { Message = ResponseMessages.InvalidToken });

            var isAdmin = IsAdminRole();

            if (!isAdmin)
            {
                var owned = await _userMonitoredService.GetMonitoredPeopleByUserIdAsync(callerId);
                if (!owned.Any(m => m.Id == request.PatientId))
                    return Forbid();
            }

            // Ensure patient exists.
            var patient = await _monitoredService.GetMonitoredPersonByIdAsync(request.PatientId);
            if (patient == null)
                return NotFound(new { Message = ResponseMessages.MonitoredPersonNotFound });

            try
            {
                var rawToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
                var tokenHash = TokenHashHelper.ComputeSha256(rawToken);

                var invitation = new Invitation
                {
                    Id = Guid.NewGuid(),
                    DoctorEmail = request.DoctorEmail.Trim(),
                    PatientId = request.PatientId,
                    Token = tokenHash,
                    ExpiresAt = DateTime.UtcNow.AddHours(24),
                    IsAccepted = false,
                    CreatedAt = DateTime.UtcNow
                };

                await _invitationRepository.AddAsync(invitation);

                var clientBase = _urlService.GetClientBaseUrl();
                var invitationLink = $"{clientBase}/invite/patient?token={Uri.EscapeDataString(rawToken)}";

                var patientDisplayName = $"{patient.FirstName} {patient.LastName}".Trim();
                await _emailService.SendDoctorInvitationEmailAsync(invitation.DoctorEmail, patientDisplayName, invitationLink);

                return Ok(new InvitationResponseDTO
                {
                    DoctorEmail = invitation.DoctorEmail,
                    PatientId = invitation.PatientId,
                    Token = rawToken,
                    ExpiresAt = invitation.ExpiresAt,
                    IsAccepted = invitation.IsAccepted
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send doctor invitation to {DoctorEmail} for patient {PatientId}", request.DoctorEmail, request.PatientId);
                return StatusCode(500, new { Message = "Failed to send invitation email. Please try again later." });
            }
        }

        [HttpPost("send-report")]
        public async Task<IActionResult> SendReport([FromBody] SendReportEmailRequestDTO request)
        {
            if (string.IsNullOrWhiteSpace(request.DoctorEmail) || string.IsNullOrWhiteSpace(request.PdfBase64))
                return BadRequest(new { Message = "Doctor email and PDF are required." });

            try
            {
                var pdfBytes = Convert.FromBase64String(request.PdfBase64);
                await _emailService.SendReportEmailAsync(request.DoctorEmail, request.PatientName, pdfBytes);
                return Ok(new { Message = "Report sent successfully." });
            }
            catch (FormatException)
            {
                return BadRequest(new { Message = "Invalid PDF data." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send report email to {DoctorEmail}", request.DoctorEmail);
                return StatusCode(500, new { Message = "Failed to send report email. Please try again later." });
            }
        }

    }
}
