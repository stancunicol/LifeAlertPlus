using LifeAlertPlus.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace LifeAlertPlus.API.Services
{
    // Serviciu de curățare a datelor vechi din DB, rulat zilnic la 03:00 UTC.
    // Gestionează trei tipuri de ștergere permanentă (hard-delete):
    //   1. Măsurători/notificări mai vechi de DataRetentionDays (implicit 365 zile) per persoană activă
    //   2. Persoane arhivate al căror ArchiveRetentionDays a expirat (cascade șterge tot)
    //   3. Persoane/conturi în soft-delete mai vechi de 7 zile (perioadă de grație)
    public class RetentionCleanupService
    {
        public const int DefaultRetentionDays = 365; // Zile de retenție implicite (dacă persoana nu are configurat)
        public const int GracePeriodDays      = 7;   // Zile de grație după soft-delete înainte de ștergere definitivă

        private readonly IServiceScopeFactory _scopeFactory; // Singleton → scope nou pentru DB
        private readonly ILogger<RetentionCleanupService> _logger;

        public RetentionCleanupService(
            IServiceScopeFactory scopeFactory,
            ILogger<RetentionCleanupService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger       = logger;
        }

        // Execută curățarea completă și returnează numărul total de înregistrări șterse
        public async Task<int> RunAsync(CancellationToken ct = default)
        {
            int totalDeleted = 0;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<LifeAlertPlusDbContext>();

                // ── PASUL 1: Persoane active — ștergem datele vechi (nu persoana!) ───────
                // Aplicăm DataRetentionDays per persoană (stocate individual pentru flexibilitate)
                var monitoreds = await db.Monitoreds
                    .Where(m => m.DeletedAt == null && !m.IsArchived)
                    .Select(m => new { m.Id, m.DataRetentionDays })
                    .ToListAsync(ct);

                var now = DateTime.UtcNow;

                foreach (var monitored in monitoreds)
                {
                    if (ct.IsCancellationRequested) break;

                    var retention = monitored.DataRetentionDays ?? DefaultRetentionDays; // 365 zile implicit
                    if (retention <= 0) continue; // 0 = retenție infinită (nu ștergem)
                    var cutoff = now.AddDays(-retention); // Data de dinaintea căreia ștergem

                    // Ștergem în loturi direct în DB (fără a încărca în memorie) — eficient pentru milioane de rânduri
                    int deletedM = await db.Measurements
                        .Where(m => m.IdMonitored == monitored.Id && m.CreatedAt < cutoff)
                        .ExecuteDeleteAsync(ct); // EF Core 7+ bulk delete

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

                    if (subtotal > 0) // Logăm doar dacă s-a șters ceva
                        _logger.LogInformation(
                            "[Retention] Monitored {Id}: retention={Days}d, deleted measurements={M}, notifications={N}, dailyHistories={D}, weeklyHistories={W}",
                            monitored.Id, retention, deletedM, deletedN, deletedD, deletedW);
                }

                // ── PASUL 2: Persoane arhivate cu ArchiveRetentionDays expirat ────────────
                // La arhivare, pacientul rămâne în DB cu datele istorice.
                // Când ArchiveRetentionDays expiră, ștergem complet persoana (cascade → toate datele)
                var archivedExpired = await db.Monitoreds
                    .Where(m => m.IsArchived
                             && m.DeletedAt == null                    // Nu e deja în soft-delete
                             && m.ArchiveRetentionDays != null          // Are perioadă de retenție configurată
                             && m.ArchivedAt != null
                             && m.ArchivedAt.Value.AddDays((double)m.ArchiveRetentionDays) < now) // A expirat
                    .ToListAsync(ct);

                if (archivedExpired.Any())
                {
                    db.Monitoreds.RemoveRange(archivedExpired); // EF Core Cascade șterge toate datele asociate
                    await db.SaveChangesAsync(ct);
                    totalDeleted += archivedExpired.Count;
                    _logger.LogInformation(
                        "[Retention] Permanently deleted {Count} archived monitored person(s) whose archive retention window expired.",
                        archivedExpired.Count);
                }

                // ── PASUL 3: Perioada de grație — soft-delete > 7 zile ───────────────────
                // Persoanele/conturile marcate ca șterse (DeletedAt != null) mai vechi de 7 zile
                // sunt șterse definitiv (adminul a avut 7 zile să reactiveze dacă a fost o eroare)
                var graceCutoff = now.AddDays(-GracePeriodDays);

                // Persoane monitorizate în soft-delete > 7 zile
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

                // Conturi de utilizatori în soft-delete > 7 zile
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
            return totalDeleted; // Numărul total de înregistrări șterse în această rulare
        }
    }
}
