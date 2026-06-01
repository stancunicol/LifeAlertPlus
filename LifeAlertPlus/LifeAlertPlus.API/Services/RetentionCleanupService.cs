using LifeAlertPlus.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace LifeAlertPlus.API.Services
{
    // Hard-deletes patient data older than the per-Monitored retention window.
    // Default retention when Monitored.DataRetentionDays is null: 365 days.
    public class RetentionCleanupService
    {
        public const int DefaultRetentionDays = 365;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RetentionCleanupService> _logger;

        public RetentionCleanupService(
            IServiceScopeFactory scopeFactory,
            ILogger<RetentionCleanupService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task<int> RunAsync(CancellationToken ct = default)
        {
            int totalDeleted = 0;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<LifeAlertPlusDbContext>();

                // Active persons: apply DataRetentionDays (measurements only).
                var monitoreds = await db.Monitoreds
                    .Where(m => m.DeletedAt == null && !m.IsArchived)
                    .Select(m => new { m.Id, m.DataRetentionDays })
                    .ToListAsync(ct);

                var now = DateTime.UtcNow;

                foreach (var monitored in monitoreds)
                {
                    if (ct.IsCancellationRequested) break;

                    var retention = monitored.DataRetentionDays ?? DefaultRetentionDays;
                    if (retention <= 0) continue;
                    var cutoff = now.AddDays(-retention);

                    int deletedM = await db.Measurements
                        .Where(m => m.IdMonitored == monitored.Id && m.CreatedAt < cutoff)
                        .ExecuteDeleteAsync(ct);

                    int deletedN = await db.Notifications
                        .Where(n => n.IdMonitored == monitored.Id && n.CreatedAt < cutoff)
                        .ExecuteDeleteAsync(ct);

                    int deletedD = await db.DailyHistories
                        .Where(d => d.IdMonitored == monitored.Id && d.Day < cutoff)
                        .ExecuteDeleteAsync(ct);

                    int deletedW = await db.WeeklyHistories
                        .Where(w => w.IdMonitored == monitored.Id && w.CreatedAt < cutoff)
                        .ExecuteDeleteAsync(ct);

                    int subtotal = deletedM + deletedN + deletedD + deletedW;
                    totalDeleted += subtotal;

                    if (subtotal > 0)
                        _logger.LogInformation(
                            "[Retention] Monitored {Id}: retention={Days}d, deleted measurements={M}, notifications={N}, dailyHistories={D}, weeklyHistories={W}",
                            monitored.Id, retention, deletedM, deletedN, deletedD, deletedW);
                }

                // Archived persons: apply ArchiveRetentionDays measured from ArchivedAt.
                // When the archive window expires the entire monitored record is hard-deleted,
                // which cascades to measurements, notifications and history via EF Core Cascade.
                var archivedExpired = await db.Monitoreds
                    .Where(m => m.IsArchived
                             && m.DeletedAt == null
                             && m.ArchiveRetentionDays != null
                             && m.ArchivedAt != null
                             && m.ArchivedAt.Value.AddDays((double)m.ArchiveRetentionDays) < now)
                    .ToListAsync(ct);

                if (archivedExpired.Any())
                {
                    db.Monitoreds.RemoveRange(archivedExpired);
                    await db.SaveChangesAsync(ct);
                    totalDeleted += archivedExpired.Count;
                    _logger.LogInformation(
                        "[Retention] Permanently deleted {Count} archived monitored person(s) whose archive retention window expired.",
                        archivedExpired.Count);
                }

                // Grace-period hard delete: Users și Monitoreds cu soft-delete > 7 zile
                // și neactivate de admin sunt șterse fizic definitiv.
                var graceCutoff = now.AddDays(-7);

                var expiredMonitoreds = await db.Monitoreds
                    .Where(m => m.DeletedAt != null && m.DeletedAt < graceCutoff)
                    .ToListAsync(ct);

                if (expiredMonitoreds.Any())
                {
                    db.Monitoreds.RemoveRange(expiredMonitoreds);
                    await db.SaveChangesAsync(ct);
                    totalDeleted += expiredMonitoreds.Count;
                    _logger.LogInformation(
                        "[Retention] Hard-deleted {Count} monitored person(s) after 7-day grace period.",
                        expiredMonitoreds.Count);
                }

                var expiredUsers = await db.Users
                    .Where(u => u.DeletedAt != null && u.DeletedAt < graceCutoff)
                    .ToListAsync(ct);

                if (expiredUsers.Any())
                {
                    db.Users.RemoveRange(expiredUsers);
                    await db.SaveChangesAsync(ct);
                    totalDeleted += expiredUsers.Count;
                    _logger.LogInformation(
                        "[Retention] Hard-deleted {Count} user account(s) after 7-day grace period.",
                        expiredUsers.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Retention cleanup job failed");
            }
            return totalDeleted;
        }

        public const int GracePeriodDays = 7;
    }
}
