using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LifeAlertPlus.API.Services;
using LifeAlertPlus.Application.IServices;

namespace LifeAlertPlus.API.Controllers
{
    // Controller pentru funcționalitățile de monitorizare în timp real:
    // predicții de tendințe vitale și testarea notificărilor.
    [ApiController]
    [Authorize] // Necesită autentificare
    [Route("api/[controller]")]
    public class MonitoringController : BaseApiController
    {
        private readonly AlertMonitorService _alertMonitorService;  // Logica de alertă și predicții
        private readonly IUserMonitoredService _userMonitoredService; // Verificare acces
        private readonly IUserService _userService;                  // Date utilizator (email, telefon)
        private readonly IEmailService _emailService;                // Trimitere email de test
        private readonly ITwilioService _twilioService;              // Trimitere SMS de test

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

        // GET /api/monitoring/{monitoredId}/predictions — Returnează predicțiile de tendințe vitale
        // Predicțiile sunt calculate din buffer-ul de 2 minute din AlertMonitorService
        // (panta tendințelor de puls, temperatură, SpO2 și dacă sunt în creștere/scădere îngrijorătoare)
        [HttpGet("{monitoredId:guid}/predictions")]
        public async Task<IActionResult> GetTrendPredictions(Guid monitoredId)
        {
            var callerId = GetCallerId();
            if (callerId == null)
                return Unauthorized();

            // Verificăm că utilizatorul are dreptul să vadă predicțiile pentru această persoană
            if (!IsAdminRole())
            {
                var owned = await _userMonitoredService.GetMonitoredPeopleByUserIdAsync(callerId.Value);
                if (!owned.Any(m => m.Id == monitoredId))
                    return Forbid();
            }

            // GetTrendPredictions returnează datele din buffer-ul de date al ultimelor 2 minute
            var result = _alertMonitorService.GetTrendPredictions(monitoredId);
            return Ok(result);
        }

        // POST /api/monitoring/test-notify — Trimite imediat un email și/sau SMS de test
        // Ocolește logica de cooldown/timing — folosit pentru a verifica configurarea SMTP și Twilio
        // Util când utilizatorul configurează prima dată notificările și vrea să confirme că funcționează
        [HttpPost("test-notify")]
        public async Task<IActionResult> TestNotify()
        {
            var callerId = GetCallerId();
            if (callerId == null)
                return Unauthorized();

            var user = await _userService.GetUserByIdAsync(callerId.Value);
            if (user == null)
                return Unauthorized();

            var results = new Dictionary<string, object>(); // Colectăm rezultatele ambelor canale

            // ── Test Email ──────────────────────────────────────────────────────────────
            if (user.NotifyByEmail)
            {
                try
                {
                    // Trimitem email de test cu același template ca alertele reale (Alert level)
                    await _emailService.SendAlertNotificationEmailAsync(
                        user.Email,
                        $"{user.FirstName} {user.LastName}".Trim(),
                        "Test Patient",  // Pacient fictiv pentru test
                        "ALERT",         // Severitate Alert (nu Critical) pentru test
                        "Acesta este un mesaj de test trimis din LifeAlertPlus pentru a verifica configurația email.",
                        user.Language ?? "ro"); // Respectăm preferința de limbă a utilizatorului
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

            // ── Test SMS (Twilio) ──────────────────────────────────────────────────────
            if (user.NotifyBySms)
            {
                if (string.IsNullOrWhiteSpace(user.PhoneNumber))
                {
                    // SMS activat dar numărul de telefon nu e configurat în profil
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

            return Ok(results); // { email: {...}, sms: {...} }
        }
    }
}
