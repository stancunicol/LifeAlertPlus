using LifeAlertPlus.API.Services;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Infrastructure.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class MonitoredConditionController : ControllerBase
    {
        private readonly IMonitoredConditionRepository _repo;
        private readonly LifeAlertPlusDbContext _db;
        private readonly ConditionRuleEngine _conditionRuleEngine;
        private readonly AlertMonitorService _alertMonitor;

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

        [HttpGet("{monitoredId}")]
        public async Task<IActionResult> Get(Guid monitoredId)
        {
            if (!await HasAccess(monitoredId)) return Forbid();

            var conditions = await _repo.GetByMonitoredIdAsync(monitoredId);
            return Ok(conditions.Select(c => c.ConditionKey));
        }

        [HttpPut("{monitoredId}")]
        public async Task<IActionResult> Replace(Guid monitoredId, [FromBody] List<string> conditionKeys)
        {
            if (!await HasAccess(monitoredId)) return Forbid();

            var valid = conditionKeys
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k.Trim().ToLowerInvariant())
                .Distinct()
                .ToList();

            await _repo.ReplaceAllAsync(monitoredId, valid);
            _conditionRuleEngine.InvalidateCache(monitoredId);

            var monitored = await _db.Monitoreds.FindAsync(monitoredId);
            if (monitored != null)
            {
                var (minHr, maxHr, minTemp, maxTemp) = ConditionThresholdAdjuster.Calculate(valid);
                monitored.MinHeartRate   = minHr;
                monitored.MaxHeartRate   = maxHr;
                monitored.MinTemperature = minTemp;
                monitored.MaxTemperature = maxTemp;
                monitored.UpdatedAt      = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                _alertMonitor.InvalidateThresholdCache(monitoredId);
            }

            return Ok(new
            {
                MinHeartRate   = monitored?.MinHeartRate,
                MaxHeartRate   = monitored?.MaxHeartRate,
                MinTemperature = monitored?.MinTemperature,
                MaxTemperature = monitored?.MaxTemperature
            });
        }

        private async Task<bool> HasAccess(Guid monitoredId)
        {
            var idStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value
                ?? User.FindFirst("nameid")?.Value;

            if (idStr == null || !Guid.TryParse(idStr, out var userId))
                return false;

            return await _db.UserMonitoreds
                .AnyAsync(um => um.IdUser == userId && um.IdMonitored == monitoredId);
        }
    }
}
