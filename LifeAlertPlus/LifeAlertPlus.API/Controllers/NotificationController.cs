using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using LifeAlertPlus.Infrastructure.Context;
using LifeAlertPlus.Shared.DTOs.Responses.Notification;
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

        [HttpGet]
        public async Task<IActionResult> GetPaged(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? type = null,
            [FromQuery] bool unreadOnly = false,
            [FromQuery] Guid? monitoredId = null)
        {
            var userId = GetCallerId();
            if (userId == null) return Forbid();

            pageSize = Math.Clamp(pageSize, 1, 50);
            page = Math.Max(1, page);

            var baseQuery = _dbContext.Notifications
                .Where(n => n.IdUser == userId.Value && n.DeletedAt == null);

            if (monitoredId.HasValue)
                baseQuery = baseQuery.Where(n => n.IdMonitored == monitoredId.Value);

            var criticalCount = await baseQuery.CountAsync(n => n.NotificationType == "Critical");
            var alertCount    = await baseQuery.CountAsync(n => n.NotificationType == "Alert");
            var unreadCount   = await baseQuery.CountAsync(n => !n.IsRead);

            var filtered = baseQuery;
            if (!string.IsNullOrWhiteSpace(type))
                filtered = filtered.Where(n => n.NotificationType == type);
            if (unreadOnly)
                filtered = filtered.Where(n => !n.IsRead);

            var totalCount = await filtered.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            var items = await filtered
                .OrderByDescending(n => n.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Join(_dbContext.Monitoreds,
                    n => n.IdMonitored,
                    m => m.Id,
                    (n, m) => new NotificationItemDTO
                    {
                        Id = n.Id,
                        NotificationType = n.NotificationType,
                        Message = n.Message,
                        CreatedAt = n.CreatedAt,
                        IdMonitored = n.IdMonitored,
                        MonitoredName = (m.FirstName + " " + m.LastName).Trim(),
                        IsRead = n.IsRead
                    })
                .ToListAsync();

            return Ok(new NotificationPagedResponseDTO
            {
                Items = items,
                TotalCount = totalCount,
                CriticalCount = criticalCount,
                AlertCount = alertCount,
                UnreadCount = unreadCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = totalPages
            });
        }

        [HttpGet("recent")]
        public async Task<IActionResult> GetRecentNotifications([FromQuery] int count = 20)
        {
            var userId = GetCallerId();
            if (userId == null) return Forbid();

            var notifications = await _dbContext.Notifications
                .Where(n => n.IdUser == userId.Value && n.DeletedAt == null)
                .OrderByDescending(n => n.CreatedAt)
                .Take(count)
                .Select(n => new { n.Id, n.NotificationType, n.Message, n.CreatedAt, n.IdMonitored, n.IsRead })
                .ToListAsync();

            return Ok(notifications);
        }

        [HttpPatch("{id}/read")]
        public async Task<IActionResult> MarkAsRead(Guid id)
        {
            var userId = GetCallerId();
            if (userId == null) return Forbid();

            var notification = await _dbContext.Notifications
                .FirstOrDefaultAsync(n => n.Id == id && n.IdUser == userId.Value && n.DeletedAt == null);

            if (notification == null) return NotFound();

            notification.IsRead = true;
            await _dbContext.SaveChangesAsync();
            return NoContent();
        }

        [HttpPatch("read-all")]
        public async Task<IActionResult> MarkAllAsRead()
        {
            var userId = GetCallerId();
            if (userId == null) return Forbid();

            var unread = await _dbContext.Notifications
                .Where(n => n.IdUser == userId.Value && !n.IsRead && n.DeletedAt == null)
                .ToListAsync();

            foreach (var n in unread)
                n.IsRead = true;

            await _dbContext.SaveChangesAsync();
            return Ok(new { Updated = unread.Count });
        }

        [HttpGet("unread-count")]
        public async Task<IActionResult> GetUnreadCount([FromQuery] string? since = null)
        {
            var userId = GetCallerId();
            if (userId == null) return Forbid();

            var sinceDate = DateTime.UtcNow.AddHours(-24);
            if (!string.IsNullOrEmpty(since) && DateTime.TryParse(since, out var parsed))
                sinceDate = parsed.ToUniversalTime();

            var count = await _dbContext.Notifications
                .Where(n => n.IdUser == userId.Value && n.DeletedAt == null && n.CreatedAt > sinceDate)
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
