using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace LifeAlertPlus.API.Services
{
    // Thin singleton that logs auditable events and system errors to the DB.
    // Fire-and-forget calls are safe here because audit writing is non-critical.
    // Serviciu singleton pentru logarea acțiunilor utilizatorilor și erorilor de sistem în baza de date
    public class AuditService
    {
        private readonly IServiceScopeFactory _scopeFactory; // Necesar deoarece serviciul e Singleton și DB-ul e Scoped
        private readonly ILogger<AuditService> _logger;

        public AuditService(IServiceScopeFactory scopeFactory, ILogger<AuditService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        // Logează o acțiune a unui utilizator (login, register, modificare date etc.)
        // Fire-and-forget: nu așteptăm finalizarea — nu blocăm fluxul principal
        public void LogAsync(string actorEmail, string action, string details, string category = "System")
            => _ = WriteAuditAsync(actorEmail, action, details, category);

        // Logează o eroare de sistem (exception neprinsă, eroare de integrare etc.)
        public void LogErrorAsync(string source, string message, string details = "", string level = "Error")
            => _ = WriteErrorAsync(source, message, details, level);

        // Metodă privată care scrie efectiv înregistrarea de audit în DB
        private async Task WriteAuditAsync(string actorEmail, string action, string details, string category)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope(); // Cream scope pentru a accesa DB din Singleton
                var db = scope.ServiceProvider.GetRequiredService<LifeAlertPlusDbContext>();
                db.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.NewGuid(),
                    Timestamp = DateTime.UtcNow, // Stocăm întotdeauna în UTC
                    ActorEmail = actorEmail, // Cine a efectuat acțiunea
                    Action = action, // Ce acțiune (ex: "Login", "UpdateProfile")
                    Details = details, // Detalii suplimentare (ex: "Changed email from X to Y")
                    Category = category // Categoria (ex: "Security", "Account", "Admin")
                });
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Nu propagăm eroarea — auditul eșuat nu trebuie să afecteze operațiunea principală
                _logger.LogWarning(ex, "Failed to write audit log: {Action}", action);
            }
        }

        // Metodă privată care scrie o eroare de sistem în DB
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
                    Level = level, // "Error", "Warning", "Critical"
                    Source = source, // URL-ul sau componenta care a generat eroarea
                    Message = message, // Mesajul scurt al excepției
                    Details = details // Stack trace complet sau detalii suplimentare
                });
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write system error log: {Message}", message);
            }
        }

        // Query helpers for the admin endpoints
        // Returnează ultimele N intrări din log-ul de audit (pentru pagina de admin)
        public async Task<List<AuditLog>> GetRecentAuditAsync(int limit = 100)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LifeAlertPlusDbContext>();
            return await db.AuditLogs
                .OrderByDescending(a => a.Timestamp) // Cele mai recente primele
                .Take(limit) // Limităm numărul de rezultate
                .ToListAsync();
        }

        // Returnează ultimele N erori de sistem (pentru pagina ErrorLog din admin)
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
