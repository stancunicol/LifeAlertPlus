using LifeAlertPlus.API.Services;
using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Shared.DTOs.Responses.ActivityProfile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LifeAlertPlus.API.Controllers
{
    // Controller pentru profilul comportamental orar al persoanei monitorizate.
    // Profilul de activitate analizează istoricul de 7 zile pentru a determina:
    // la ce ore persoana e activă, la ce ore doarme și care e pulsul mediu pe oră.
    // Folosit pentru detectarea anomaliilor: "de obicei activ la 10:00, dar azi nu s-a mișcat".
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // Necesită autentificare
    public class ActivityProfileController : BaseApiController
    {
        private readonly ActivityProfileService _activityProfileService; // Logica de construire și analiză a profilului
        private readonly IUserMonitoredService _userMonitoredService; // Verifică dreptul de acces al utilizatorului

        public ActivityProfileController(ActivityProfileService activityProfileService, IUserMonitoredService userMonitoredService)
        {
            _activityProfileService = activityProfileService;
            _userMonitoredService = userMonitoredService;
        }

        // Verifică că utilizatorul curent are dreptul să acceseze profilul persoanei monitorizate
        private async Task<bool> UserOwnsMonitoredAsync(Guid monitoredId)
        {
            if (IsAdminRole()) return true; // Adminul vede totul
            var callerId = GetCallerId();
            if (callerId == null) return false;
            var owned = await _userMonitoredService.GetMonitoredPeopleByUserIdAsync(callerId.Value);
            return owned.Any(m => m.Id == monitoredId); // Verifică că persoana e în lista utilizatorului
        }

        // GET /api/activityprofile/{monitoredId} — Returnează profilul orar de activitate
        // Răspuns: HourlyProfiles[24] cu rata de mișcare, probabilitatea de somn și pulsul mediu pe oră
        [HttpGet("{monitoredId}")]
        public async Task<IActionResult> GetProfile(Guid monitoredId)
        {
            if (monitoredId == Guid.Empty)
                return BadRequest(new { Message = "Invalid monitored ID." });

            if (!await UserOwnsMonitoredAsync(monitoredId))
                return Forbid(); // 403 — utilizatorul nu are acces la această persoană

            var profiles = await _activityProfileService.GetProfileAsync(monitoredId);

            // Profilul nu există încă — se construiește în fundal (nicio dată sau build în curs)
            if (profiles.Count == 0)
                return NotFound(new { Message = "No activity profile found. A build may be in progress." });

            // Transformăm entitățile DB în DTO-uri cu etichetă lizibilă ("Activ", "Somn" etc.)
            var response = new ActivityProfileResponseDTO
            {
                IdMonitored = monitoredId,
                LastUpdated = profiles.Max(p => p.LastUpdated), // Cea mai recentă actualizare a profilului
                HourlyProfiles = profiles.Select(p => new HourlyProfileDTO
                {
                    HourOfDay        = p.HourOfDay,
                    AveragePulse     = p.AveragePulse,     // Pulsul mediu al persoanei la această oră
                    MovementRate     = p.MovementRate,     // Proporția timpului în care persoana se mișcă (0-1)
                    SleepProbability = p.SleepProbability, // Probabilitatea ca persoana să doarmă la această oră (0-1)
                    DataPoints       = p.DataPoints,       // Numărul de măsurători care au stat la baza profilului
                    Label            = GetLabel(p.MovementRate, p.SleepProbability, p.DataPoints) // Etichetă text
                }).ToList()
            };

            return Ok(response);
        }

        // POST /api/activityprofile/{monitoredId}/build — Declanșează manual construirea profilului
        // Rulează în background (fire-and-forget) — clientul primește 202 Accepted imediat,
        // nu trebuie să aștepte (build-ul poate dura câteva secunde pentru mulți pacienți)
        [HttpPost("{monitoredId}/build")]
        public async Task<IActionResult> TriggerBuild(Guid monitoredId)
        {
            if (monitoredId == Guid.Empty)
                return BadRequest(new { Message = "Invalid monitored ID." });

            if (!await UserOwnsMonitoredAsync(monitoredId))
                return Forbid();

            // Fire-and-forget: pornim build-ul fără să așteptăm rezultatul
            _ = Task.Run(() => _activityProfileService.BuildProfileAsync(monitoredId));

            return Accepted(new { Message = "Profile build started." }); // 202 — procesul rulează în fundal
        }

        // Convertește datele numerice ale profilului în etichetă text lizibilă pentru UI
        // Regulile sunt ierarhizate: date insuficiente > somn > activ > moderat > inactiv
        private static string GetLabel(double movementRate, double sleepProbability, int dataPoints)
        {
            if (dataPoints < 10) return "Date insuficiente"; // Prea puține măsurători pentru o concluzie
            if (sleepProbability > 0.60) return "Somn";       // >60% din timp stă culcat cu puls mic
            if (movementRate > 0.60) return "Activ";           // >60% din timp se mișcă
            if (movementRate > 0.30) return "Moderat activ";   // 30-60% din timp se mișcă
            return "Inactiv / Odihnă";                         // <30% mișcare, nu doarme
        }
    }
}
