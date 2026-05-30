using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace LifeAlertPlus.API.Services
{
    // Thin singleton that logs auditable events and system errors to the DB.
    // Fire-and-forget calls are safe here because audit writing is non-critical.
    public class AuditService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AuditService> _logger;

        public AuditService(IServiceScopeFactory scopeFactory, ILogger<AuditService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public void LogAsync(string actorEmail, string action, string details, string category = "System")
            => _ = WriteAuditAsync(actorEmail, action, details, category);

        public void LogErrorAsync(string source, string message, string details = "", string level = "Error")
            => _ = WriteErrorAsync(source, message, details, level);

        private async Task WriteAuditAsync(string actorEmail, string action, string details, string category)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<LifeAlertPlusDbContext>();
                db.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.NewGuid(),
                    Timestamp = DateTime.UtcNow,
                    ActorEmail = actorEmail,
                    Action = action,
                    Details = details,
                    Category = category
                });
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write audit log: {Action}", action);
            }
        }

        private async Task WriteErrorAsync(string source, string message, string details, string level)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<LifeAlertPlusDbContext>();
                db.SystemErrors.Add(new SystemError
                {
                    Id = Guid.NewGuid(),
                    Timestamp = DateTime.UtcNow,
                    Level = level,
                    Source = source,
                    Message = message,
                    Details = details
                });
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write system error log: {Message}", message);
            }
        }

        // Query helpers for the admin endpoints
        public async Task<List<AuditLog>> GetRecentAuditAsync(int limit = 100)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LifeAlertPlusDbContext>();
            return await db.AuditLogs
                .OrderByDescending(a => a.Timestamp)
                .Take(limit)
                .ToListAsync();
        }

        public async Task<List<SystemError>> GetRecentErrorsAsync(int limit = 100)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LifeAlertPlusDbContext>();
            return await db.SystemErrors
                .OrderByDescending(e => e.Timestamp)
                .Take(limit)
                .ToListAsync();
        }
    }
}
