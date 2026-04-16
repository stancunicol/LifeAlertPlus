using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using LifeAlertPlus.Infrastructure.Context;
using System.Security.Claims;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class NotificationController : ControllerBase
    {
        private readonly LifeAlertPlusDbContext _dbContext;

        public NotificationController(LifeAlertPlusDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet("recent")]
        public async Task<IActionResult> GetRecentNotifications([FromQuery] int count = 20)
        {
            var userId = GetCallerId();
            if (userId == null) return Forbid();

            var monitoredIds = await _dbContext.UserMonitoreds
                .Where(um => um.IdUser == userId.Value)
                .Select(um => um.IdMonitored)
                .ToListAsync();

            var notifications = await _dbContext.Notifications
                .Where(n => monitoredIds.Contains(n.IdMonitored) && n.DeletedAt == null)
                .OrderByDescending(n => n.CreatedAt)
                .Take(count)
                .Select(n => new
                {
                    n.Id,
                    n.NotificationType,
                    n.Message,
                    n.CreatedAt,
                    n.IdMonitored
                })
                .ToListAsync();

            return Ok(notifications);
        }

        [HttpGet("unread-count")]
        public async Task<IActionResult> GetUnreadCount([FromQuery] string? since = null)
        {
            var userId = GetCallerId();
            if (userId == null) return Forbid();

            var sinceDate = DateTime.UtcNow.AddHours(-24);
            if (!string.IsNullOrEmpty(since) && DateTime.TryParse(since, out var parsed))
                sinceDate = parsed.ToUniversalTime();

            var monitoredIds = await _dbContext.UserMonitoreds
                .Where(um => um.IdUser == userId.Value)
                .Select(um => um.IdMonitored)
                .ToListAsync();

            var count = await _dbContext.Notifications
                .Where(n => monitoredIds.Contains(n.IdMonitored) && n.DeletedAt == null && n.CreatedAt > sinceDate)
                .CountAsync();

            return Ok(new { Count = count });
        }

        private Guid? GetCallerId()
        {
            var idStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value
                ?? User.FindFirst("nameid")?.Value;
            return idStr != null && Guid.TryParse(idStr, out var id) ? id : null;
        }
    }
}
