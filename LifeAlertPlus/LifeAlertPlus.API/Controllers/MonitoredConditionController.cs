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

        public MonitoredConditionController(IMonitoredConditionRepository repo, LifeAlertPlusDbContext db, ConditionRuleEngine conditionRuleEngine)
        {
            _repo = repo;
            _db = db;
            _conditionRuleEngine = conditionRuleEngine;
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
                .Distinct();

            await _repo.ReplaceAllAsync(monitoredId, valid);
            _conditionRuleEngine.InvalidateCache(monitoredId);
            return NoContent();
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
