using System.Linq;
using System.Security.Claims;
using LifeAlertPlus.API.Helpers;
using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Shared.DTOs.Requests.Email;
using LifeAlertPlus.Shared.DTOs.Responses.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
        private readonly ILogger<InvitationsController> _logger;

        public InvitationsController(
            IInvitationRepository invitationRepository,
            IUserMonitoredService userMonitoredService,
            IMonitoredService monitoredService,
            IMeasurementService measurementService,
            ILogger<InvitationsController> logger)
        {
            _invitationRepository = invitationRepository;
            _userMonitoredService = userMonitoredService;
            _monitoredService = monitoredService;
            _measurementService = measurementService;
            _logger = logger;
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

            var response = new InvitationInfoResponseDTO
            {
                DoctorEmail = invitation.DoctorEmail,
                PatientId = invitation.PatientId,
                PatientName = patientName,
                ExpiresAt = invitation.ExpiresAt,
                IsAccepted = invitation.IsAccepted,
                IsExpired = invitation.ExpiresAt < DateTime.UtcNow
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
