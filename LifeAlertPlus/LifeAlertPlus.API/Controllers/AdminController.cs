using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LifeAlertPlus.API.Services;
using LifeAlertPlus.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace LifeAlertPlus.API.Controllers
{
    // Controller pentru panoul de administrare al LifeAlertPlus.
    // Accesibil EXCLUSIV administratorilor — expune vizibilitate completă asupra sistemului:
    // starea dispozitivelor ESP, logul de audit și logul de erori.
    [ApiController]
    [Authorize(Roles = "Admin")] // Toate endpoint-urile necesită rolul Admin
    [Route("api/[controller]")]
    public class AdminController(AuditService auditService, SimulationManager simulationManager, LifeAlertPlusDbContext db) : ControllerBase
    {
        // GET /api/admin/device-status — Returnează starea tuturor dispozitivelor ESP înregistrate
        // Determină dacă un dispozitiv e online pe baza datei ultimelor date primite (timestamp freshness)
        [HttpGet("device-status")]
        public async Task<IActionResult> GetDeviceStatus()
        {
            // Citim toate persoanele monitorizate care au un număr de serie de dispozitiv configurat
            var devices = await db.Monitoreds
                .Where(m => !string.IsNullOrWhiteSpace(m.DeviceSerialNumber))
                .Select(m => new { m.Id, m.FirstName, m.LastName, m.DeviceSerialNumber, m.IsArchived, m.DeletedAt, m.UpdateFrequency })
                .ToListAsync();

            var nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds(); // Momentul curent ca Unix timestamp (secunde)

            var result = devices.Select(d =>
            {
                // Citim ultimele date ESP primite (din SimulationManager — fie reale, fie simulate)
                var espData = simulationManager.GetData(d.DeviceSerialNumber!);
                // Citim ultimul heartbeat primit de la dispozitiv (semnal de viață separat de date)
                var hb = simulationManager.GetHeartbeat(d.DeviceSerialNumber!);

                // Pragul de "freshness": dacă datele sunt mai vechi decât pragul, dispozitivul e considerat offline
                // Formula: max(180s, UpdateFrequency * 2 + 60s) — dacă trimite la 30s, pragul e 120s
                var freshnessThreshold = Math.Max(180, (d.UpdateFrequency ?? 60) * 2 + 60);

                return new
                {
                    d.Id,
                    PatientName        = $"{d.FirstName} {d.LastName}".Trim(),
                    d.DeviceSerialNumber,
                    d.IsArchived,
                    IsDeleted          = d.DeletedAt != null, // Marcat pentru ștergere (soft-delete)
                    d.DeletedAt,
                    // Dispozitivul e online dacă: contul nu e șters, datele există, sunt "available"
                    // și timestamp-ul e mai recent decât pragul de freshness
                    IsOnline           = d.DeletedAt == null && espData != null && espData.IsAvailable
                                         && (espData.Date <= 0 || (nowSec - espData.Date) < freshnessThreshold),
                    Battery            = espData?.Battery,          // Nivelul bateriei (0-100%)
                    RssiDbm            = hb?.Data.RssiDbm,          // Puterea semnalului WiFi în dBm
                    UptimeSeconds      = hb?.Data.UptimeSeconds,    // Câte secunde rulează dispozitivul fără restart
                    HeartbeatAgeSec    = hb.HasValue                // Câte secunde au trecut de la ultimul heartbeat
                        ? (int)(DateTime.UtcNow - hb.Value.ReceivedAt).TotalSeconds
                        : (int?)null,
                    LastDataDate       = espData?.Date              // Unix timestamp al ultimelor date primite
                };
            });
            return Ok(result);
        }

        // GET /api/admin/audit-log?limit=100 — Returnează ultimele N înregistrări din logul de audit
        // Logul de audit conține acțiunile utilizatorilor: login, creare pacient, ștergere cont etc.
        [HttpGet("audit-log")]
        public async Task<IActionResult> GetAuditLog([FromQuery] int limit = 100)
        {
            limit = Math.Clamp(limit, 1, 500); // Prevenim interogări prea mari (maxim 500 înregistrări)
            var entries = await auditService.GetRecentAuditAsync(limit);
            return Ok(entries.Select(e => new
            {
                e.Id,
                e.Timestamp, // Momentul acțiunii (UTC)
                User     = e.ActorEmail, // Email-ul utilizatorului care a efectuat acțiunea
                e.Action,    // Ex: "Login", "DeleteAccount", "AddPatient"
                e.Details,   // Detalii suplimentare despre acțiune
                e.Category   // Categoria acțiunii: "Auth", "Patient", "Account" etc.
            }));
        }

        // GET /api/admin/error-log?limit=100 — Returnează ultimele N erori din logul de erori
        // Erorile sunt înregistrate de AuditService.WriteErrorAsync la excepții neprevăzute
        [HttpGet("error-log")]
        public async Task<IActionResult> GetErrorLog([FromQuery] int limit = 100)
        {
            limit = Math.Clamp(limit, 1, 500);
            var entries = await auditService.GetRecentErrorsAsync(limit);
            return Ok(entries.Select(e => new
            {
                e.Timestamp, // Momentul erorii (UTC)
                e.Level,     // Severitatea: "Error", "Critical"
                e.Source,    // Clasa sau serviciul care a generat eroarea
                e.Message,   // Mesajul de eroare
                e.Details    // Stack trace sau detalii suplimentare
            }));
        }
    }
}
