using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LifeAlertPlus.API.Services;
using LifeAlertPlus.Application.IServices;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class MonitoringController : BaseApiController
    {
        private readonly AlertMonitorService _alertMonitorService;
        private readonly IUserMonitoredService _userMonitoredService;
        private readonly IUserService _userService;
        private readonly IEmailService _emailService;
        private readonly ITwilioService _twilioService;

        public MonitoringController(
            AlertMonitorService alertMonitorService,
            IUserMonitoredService userMonitoredService,
            IUserService userService,
            IEmailService emailService,
            ITwilioService twilioService)
        {
            _alertMonitorService = alertMonitorService;
            _userMonitoredService = userMonitoredService;
            _userService = userService;
            _emailService = emailService;
            _twilioService = twilioService;
        }

        [HttpGet("{monitoredId:guid}/predictions")]
        public async Task<IActionResult> GetTrendPredictions(Guid monitoredId)
        {
            var callerId = GetCallerId();
            if (callerId == null)
                return Unauthorized();

            var owned = await _userMonitoredService.GetMonitoredPeopleByUserIdAsync(callerId.Value);
            if (!owned.Any(m => m.Id == monitoredId))
                return Forbid();

            var result = _alertMonitorService.GetTrendPredictions(monitoredId);
            return Ok(result);
        }

        /// <summary>
        /// Sends a test email and/or SMS to the authenticated user immediately,
        /// bypassing all alert timing logic. Useful for verifying SMTP / Twilio config.
        /// </summary>
        [HttpPost("test-notify")]
        public async Task<IActionResult> TestNotify()
        {
            var callerId = GetCallerId();
            if (callerId == null)
                return Unauthorized();

            var user = await _userService.GetUserByIdAsync(callerId.Value);
            if (user == null)
                return Unauthorized();

            var results = new Dictionary<string, object>();

            // Email
            if (user.NotifyByEmail)
            {
                try
                {
                    await _emailService.SendAlertNotificationEmailAsync(
                        user.Email,
                        $"{user.FirstName} {user.LastName}".Trim(),
                        "Test Patient",
                        "ALERT",
                        "Acesta este un mesaj de test trimis din LifeAlertPlus pentru a verifica configurația email.",
                        user.Language ?? "ro");
                    results["email"] = new { success = true, to = user.Email };
                }
                catch (Exception ex)
                {
                    results["email"] = new { success = false, error = ex.Message, to = user.Email };
                }
            }
            else
            {
                results["email"] = new { success = false, error = "NotifyByEmail is disabled for this user." };
            }

            // SMS
            if (user.NotifyBySms)
            {
                if (string.IsNullOrWhiteSpace(user.PhoneNumber))
                {
                    results["sms"] = new { success = false, error = "No phone number configured for this user." };
                }
                else
                {
                    try
                    {
                        await _twilioService.SendSmsAsync(user.PhoneNumber, "LifeAlertPlus: mesaj de test pentru verificarea configurației SMS.");
                        results["sms"] = new { success = true, to = user.PhoneNumber };
                    }
                    catch (Exception ex)
                    {
                        results["sms"] = new { success = false, error = ex.Message, to = user.PhoneNumber };
                    }
                }
            }
            else
            {
                results["sms"] = new { success = false, error = "NotifyBySms is disabled for this user. Enable it in Settings." };
            }

            return Ok(results);
        }
    }
}
