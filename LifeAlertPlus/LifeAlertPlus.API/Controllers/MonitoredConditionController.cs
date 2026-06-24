using LifeAlertPlus.API.Services;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Infrastructure.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LifeAlertPlus.API.Controllers
{
    // Controller pentru gestionarea condițiilor medicale ale persoanelor monitorizate.
    // Condițiile (ex. "hypertension", "arrhythmia", "copd") influențează:
    // 1. Regulile de alertă din ConditionRuleEngine (praguri individualizate per boală)
    // 2. Pragurile vitale MinHR/MaxHR/MinSpO2 etc. stocate pe Monitored (via ConditionThresholdAdjuster)
    [ApiController]
    [Authorize] // Necesită autentificare
    [Route("api/[controller]")]
    public class MonitoredConditionController : BaseApiController
    {
        private readonly IMonitoredConditionRepository _repo;              // Acces DB la tabelul condiții
        private readonly LifeAlertPlusDbContext _db;                       // Context EF Core direct (pentru update praguri)
        private readonly ConditionRuleEngine _conditionRuleEngine;         // Cache-ul regulilor per pacient (invalidat la modificare)
        private readonly AlertMonitorService _alertMonitor;                // Cache-ul pragurilor din alert monitor (invalidat la modificare)

        public MonitoredConditionController(
            IMonitoredConditionRepository repo,
            LifeAlertPlusDbContext db,
            ConditionRuleEngine conditionRuleEngine,
            AlertMonitorService alertMonitor)
        {
            _repo = repo;
            _db = db;
            _conditionRuleEngine = conditionRuleEngine;
            _alertMonitor = alertMonitor;
        }

        // GET /api/monitoredcondition/{monitoredId} — Returnează lista cheilor de condiții medicale
        // Exemplu răspuns: ["hypertension", "arrhythmia", "diabetes"]
        [HttpGet("{monitoredId}")]
        public async Task<IActionResult> Get(Guid monitoredId)
        {
            if (!await HasAccess(monitoredId)) return Forbid();

            var conditions = await _repo.GetByMonitoredIdAsync(monitoredId);
            return Ok(conditions.Select(c => c.ConditionKey)); // Returnăm doar cheile text, nu entitățile complete
        }

        // PUT /api/monitoredcondition/{monitoredId} — Înlocuiește COMPLET lista de condiții medicale
        // (operație de tip "replace-all" — nu e adăugare incrementală)
        // Actualizează și pragurile vitale MinHR/MaxHR/SpO2 pe Monitored conform bolilor selectate
        [HttpPut("{monitoredId}")]
        public async Task<IActionResult> Replace(Guid monitoredId, [FromBody] List<string> conditionKeys)
        {
            if (!await HasAccess(monitoredId)) return Forbid();

            // Normalizăm cheile: eliminăm spații, lowercase, eliminăm duplicate
            var valid = conditionKeys
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k.Trim().ToLowerInvariant())
                .Distinct()
                .ToList();

            await _repo.ReplaceAllAsync(monitoredId, valid); // Ștergem tot ce era și inserăm noile condiții

            // Invalidăm cache-ul din ConditionRuleEngine (va reciti regulile la următoarea alertă)
            _conditionRuleEngine.InvalidateCache(monitoredId);

            // Recalculăm pragurile vitale recomandate pe baza noilor condiții și le salvăm pe entitatea Monitored
            var monitored = await _db.Monitoreds.FindAsync(monitoredId);
            if (monitored != null)
            {
                // ConditionThresholdAdjuster calculează pragurile optime: ex. BPOC → MinSpO2 = 88%
                var (minHr, maxHr, minTemp, maxTemp, minSpO2, maxSpO2) = ConditionThresholdAdjuster.Calculate(valid);
                monitored.MinHeartRate   = minHr;
                monitored.MaxHeartRate   = maxHr;
                monitored.MinTemperature = minTemp;
                monitored.MaxTemperature = maxTemp;
                monitored.MinSpO2        = minSpO2;
                monitored.MaxSpO2        = maxSpO2;
                monitored.UpdatedAt      = DateTime.UtcNow;
                await _db.SaveChangesAsync();

                // Invalidăm și cache-ul de praguri din AlertMonitorService (va citi noile praguri din DB)
                _alertMonitor.InvalidateThresholdCache(monitoredId);
            }

            // Returnăm pragurile noi calculate pentru ca frontend-ul să le poată afișa
            return Ok(new
            {
                MinHeartRate   = monitored?.MinHeartRate,
                MaxHeartRate   = monitored?.MaxHeartRate,
                MinTemperature = monitored?.MinTemperature,
                MaxTemperature = monitored?.MaxTemperature,
                MinSpO2        = monitored?.MinSpO2,
                MaxSpO2        = monitored?.MaxSpO2
            });
        }

        // Verifică dacă utilizatorul curent are drept de acces la condiițiile persoanei monitorizate
        private async Task<bool> HasAccess(Guid monitoredId)
        {
            if (IsAdminRole()) return true; // Adminul are acces la toate
            var userId = GetCallerId();
            if (userId == null) return false;
            // Verificăm că există o legătură UserMonitored între utilizator și persoana monitorizată
            return await _db.UserMonitoreds
                .AnyAsync(um => um.IdUser == userId.Value && um.IdMonitored == monitoredId);
        }
    }
}
