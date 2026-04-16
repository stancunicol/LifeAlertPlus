using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Shared.DTOs.Requests.Email;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class EmailController : ControllerBase
    {
        private readonly IEmailService _emailService;

        public EmailController(IEmailService emailService)
        {
            _emailService = emailService;
        }

        [HttpPost("send-report")]
        public async Task<IActionResult> SendReport([FromBody] SendReportEmailRequestDTO request)
        {
            if (string.IsNullOrWhiteSpace(request.DoctorEmail) || string.IsNullOrWhiteSpace(request.PdfBase64))
                return BadRequest("Doctor email and PDF are required.");

            try
            {
                var pdfBytes = Convert.FromBase64String(request.PdfBase64);
                await _emailService.SendReportEmailAsync(request.DoctorEmail, request.PatientName, pdfBytes);
                return Ok(new { Message = "Report sent successfully." });
            }
            catch (FormatException)
            {
                return BadRequest("Invalid PDF data.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Failed to send email: {ex.Message}");
            }
        }
    }
}
