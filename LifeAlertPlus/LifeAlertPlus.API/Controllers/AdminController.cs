using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LifeAlertPlus.API.Services;
using LifeAlertPlus.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Authorize(Roles = "Admin")]
    [Route("api/[controller]")]
    public class AdminController(AuditService auditService, SimulationManager simulationManager, LifeAlertPlusDbContext db) : ControllerBase
    {
        [HttpGet("device-status")]
        public async Task<IActionResult> GetDeviceStatus()
        {
            var devices = await db.Monitoreds
                .Where(m => !string.IsNullOrWhiteSpace(m.DeviceSerialNumber))
                .Select(m => new { m.Id, m.FirstName, m.LastName, m.DeviceSerialNumber, m.IsArchived, m.DeletedAt, m.UpdateFrequency })
                .ToListAsync();

            var nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var result = devices.Select(d =>
            {
                var espData = simulationManager.GetData(d.DeviceSerialNumber!);
                var hb = simulationManager.GetHeartbeat(d.DeviceSerialNumber!);
                var freshnessThreshold = Math.Max(180, (d.UpdateFrequency ?? 60) * 2 + 60);
                return new
                {
                    d.Id,
                    PatientName = $"{d.FirstName} {d.LastName}".Trim(),
                    d.DeviceSerialNumber,
                    d.IsArchived,
                    IsDeleted = d.DeletedAt != null,
                    d.DeletedAt,
                    IsOnline = d.DeletedAt == null && espData != null && espData.IsAvailable
                        && (espData.Date <= 0 || (nowSec - espData.Date) < freshnessThreshold),
                    Battery = espData?.Battery,
                    RssiDbm = hb?.Data.RssiDbm,
                    UptimeSeconds = hb?.Data.UptimeSeconds,
                    HeartbeatAgeSec = hb.HasValue ? (int)(DateTime.UtcNow - hb.Value.ReceivedAt).TotalSeconds : (int?)null,
                    LastDataDate = espData?.Date
                };
            });
            return Ok(result);
        }


        [HttpGet("audit-log")]
        public async Task<IActionResult> GetAuditLog([FromQuery] int limit = 100)
        {
            limit = Math.Clamp(limit, 1, 500);
            var entries = await auditService.GetRecentAuditAsync(limit);
            return Ok(entries.Select(e => new
            {
                e.Id,
                e.Timestamp,
                User     = e.ActorEmail,
                e.Action,
                e.Details,
                e.Category
            }));
        }

        [HttpGet("error-log")]
        public async Task<IActionResult> GetErrorLog([FromQuery] int limit = 100)
        {
            limit = Math.Clamp(limit, 1, 500);
            var entries = await auditService.GetRecentErrorsAsync(limit);
            return Ok(entries.Select(e => new
            {
                e.Timestamp,
                e.Level,
                e.Source,
                e.Message,
                e.Details
            }));
        }
    }
}
