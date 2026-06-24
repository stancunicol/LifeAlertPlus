using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using LifeAlertPlus.Infrastructure.Context;
using LifeAlertPlus.Shared.DTOs.Requests.Notification;
using LifeAlertPlus.Shared.DTOs.Responses.Notification;

namespace LifeAlertPlus.API.Controllers
{
    // Controller pentru gestionarea notificărilor de alertă medicală ale utilizatorului.
    // Notificările sunt create de AlertMonitorService și stocate în DB;
    // acest controller le expune clientului cu paginare, filtrare și marcare citit.
    // Include și sistemul de feedback "fals alarm" — utilizatorul poate confirma dacă alerta a fost reală.
    [ApiController]
    [Authorize] // Fiecare utilizator vede doar propriile notificări
    [Route("api/[controller]")]
    public class NotificationController : BaseApiController
    {
        private readonly LifeAlertPlusDbContext _dbContext; // Acces direct la DB (EF Core)

        public NotificationController(LifeAlertPlusDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        // GET /api/notification?page=1&pageSize=10&type=Critical&unreadOnly=false&monitoredId=...
        // Returnează notificările paginat cu numărătoare totale pentru fiecare tip
        [HttpGet]
        public async Task<IActionResult> GetPaged(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? type = null,          // Filtru: "Critical", "Alert", "Info" sau null (toate)
            [FromQuery] bool unreadOnly = false,       // Filtru: returnează doar notificările necitite
            [FromQuery] Guid? monitoredId = null)      // Filtru: notificările pentru o persoană specifică
        {
            var userId = GetCallerId();
            if (userId == null) return Forbid();

            pageSize = Math.Clamp(pageSize, 1, 50); // Limităm la maxim 50 pe pagină
            page = Math.Max(1, page);               // Minim pagina 1

            // Query de bază: notificările utilizatorului curent care nu au fost șterse (soft-delete)
            var baseQuery = _dbContext.Notifications
                .Where(n => n.IdUser == userId.Value && n.DeletedAt == null);

            // Filtru opțional pe persoana monitorizată
            if (monitoredId.HasValue)
                baseQuery = baseQuery.Where(n => n.IdMonitored == monitoredId.Value);

            // Calculăm numărătorile (Critical, Alert, necitite) dintr-un singur query GROUP BY
            // Aceasta evită 3 interogări separate și reduce latența
            var counts = await baseQuery
                .GroupBy(_ => 1) // Grupăm totul într-un singur grup
                .Select(g => new
                {
                    CriticalCount = g.Count(n => n.NotificationType == "Critical"),
                    AlertCount    = g.Count(n => n.NotificationType == "Alert"),
                    UnreadCount   = g.Count(n => !n.IsRead)
                })
                .FirstOrDefaultAsync();
            var criticalCount = counts?.CriticalCount ?? 0;
            var alertCount    = counts?.AlertCount    ?? 0;
            var unreadCount   = counts?.UnreadCount   ?? 0;

            // Aplicăm filtrele de tip și citit/necitit
            var filtered = baseQuery;
            if (!string.IsNullOrWhiteSpace(type))
                filtered = filtered.Where(n => n.NotificationType == type); // Filtru pe tip
            if (unreadOnly)
                filtered = filtered.Where(n => !n.IsRead); // Doar necitite

            var totalCount = await filtered.CountAsync(); // Numărul total pentru paginare
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            // JOIN cu Monitoreds pentru a include numele persoanei monitorizate în răspuns
            var items = await filtered
                .OrderByDescending(n => n.CreatedAt) // Cele mai recente primele
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Join(_dbContext.Monitoreds,
                    n => n.IdMonitored,
                    m => m.Id,
                    (n, m) => new NotificationItemDTO
                    {
                        Id               = n.Id,
                        NotificationType = n.NotificationType, // "Critical", "Alert", "Info"
                        Message          = n.Message,           // Textul alertei
                        CreatedAt        = n.CreatedAt,
                        IdMonitored      = n.IdMonitored,
                        MonitoredName    = (m.FirstName + " " + m.LastName).Trim(), // Numele pacientului
                        IsRead           = n.IsRead
                    })
                .ToListAsync();

            return Ok(new NotificationPagedResponseDTO
            {
                Items         = items,
                TotalCount    = totalCount,
                CriticalCount = criticalCount,
                AlertCount    = alertCount,
                UnreadCount   = unreadCount,
                Page          = page,
                PageSize      = pageSize,
                TotalPages    = totalPages
            });
        }

        // GET /api/notification/recent?count=20 — Ultimele N notificări (fără paginare)
        // Folosit de header-ul aplicației pentru a afișa notificările recente în dropdown
        [HttpGet("recent")]
        public async Task<IActionResult> GetRecentNotifications([FromQuery] int count = 20)
        {
            var userId = GetCallerId();
            if (userId == null) return Forbid();

            var notifications = await _dbContext.Notifications
                .Where(n => n.IdUser == userId.Value && n.DeletedAt == null)
                .OrderByDescending(n => n.CreatedAt)
                .Take(count) // Luăm doar primele N
                .Select(n => new { n.Id, n.NotificationType, n.Message, n.CreatedAt, n.IdMonitored, n.IsRead })
                .ToListAsync();

            return Ok(notifications);
        }

        // PATCH /api/notification/{id}/read — Marchează o notificare ca citită
        [HttpPatch("{id}/read")]
        public async Task<IActionResult> MarkAsRead(Guid id)
        {
            var userId = GetCallerId();
            if (userId == null) return Forbid();

            // Verificăm că notificarea aparține utilizatorului curent (securitate)
            var notification = await _dbContext.Notifications
                .FirstOrDefaultAsync(n => n.Id == id && n.IdUser == userId.Value && n.DeletedAt == null);

            if (notification == null) return NotFound();

            notification.IsRead = true;
            await _dbContext.SaveChangesAsync();
            return NoContent(); // 204 — operație reușită fără corp de răspuns
        }

        // PATCH /api/notification/read-all — Marchează TOATE notificările utilizatorului ca citite
        [HttpPatch("read-all")]
        public async Task<IActionResult> MarkAllAsRead()
        {
            var userId = GetCallerId();
            if (userId == null) return Forbid();

            // Citim toate notificările necitite ale utilizatorului în memorie
            var unread = await _dbContext.Notifications
                .Where(n => n.IdUser == userId.Value && !n.IsRead && n.DeletedAt == null)
                .ToListAsync();

            foreach (var n in unread)
                n.IsRead = true; // Marcăm fiecare ca citit

            await _dbContext.SaveChangesAsync();
            return Ok(new { Updated = unread.Count }); // Informăm clientul câte au fost marcate
        }

        // GET /api/notification/pending-feedback — Notificările care așteaptă feedback de la utilizator
        // Sistemul de feedback: după o alertă, utilizatorul poate confirma dacă a fost o alertă reală
        // sau un fals pozitiv — ajută la îmbunătățirea sistemului de detectare
        [HttpGet("pending-feedback")]
        public async Task<IActionResult> GetPendingFeedback()
        {
            var userId = GetCallerId();
            if (userId == null) return Forbid();

            var items = await _dbContext.Notifications
                .Where(n => n.IdUser == userId.Value
                    && n.DeletedAt == null
                    && n.FeedbackRequestedAt != null // Alertele pentru care s-a solicitat feedback
                    && n.WasReal == null)             // Utilizatorul nu a răspuns încă
                .OrderBy(n => n.FeedbackRequestedAt)
                .Join(_dbContext.Monitoreds,
                    n => n.IdMonitored,
                    m => m.Id,
                    (n, m) => new PendingFeedbackDTO
                    {
                        Id               = n.Id,
                        IdMonitored      = m.Id,
                        NotificationType = n.NotificationType,
                        Message          = n.Message,
                        CreatedAt        = n.CreatedAt,
                        MonitoredName    = (m.FirstName + " " + m.LastName).Trim()
                    })
                .ToListAsync();

            return Ok(items);
        }

        // PATCH /api/notification/{id}/feedback — Utilizatorul confirmă dacă alerta a fost reală sau nu
        // WasReal=true → alertă validă; WasReal=false → fals pozitiv (poate fi folosit pentru ML training)
        [HttpPatch("{id}/feedback")]
        public async Task<IActionResult> SubmitFeedback(Guid id, [FromBody] NotificationFeedbackRequestDTO body)
        {
            var userId = GetCallerId();
            if (userId == null) return Forbid();
            if (body == null) return BadRequest();

            // Verificăm că notificarea există, aparține utilizatorului și are feedback solicitat
            var notification = await _dbContext.Notifications
                .FirstOrDefaultAsync(n => n.Id == id
                    && n.IdUser == userId.Value
                    && n.DeletedAt == null
                    && n.FeedbackRequestedAt != null); // Numai alertele care au cerut feedback

            if (notification == null) return NotFound();

            notification.WasReal = body.WasReal;                    // true = reală, false = fals pozitiv
            notification.FeedbackRespondedAt = DateTime.UtcNow;     // Momentul răspunsului
            await _dbContext.SaveChangesAsync();
            return NoContent();
        }

        // GET /api/notification/unread-count?since=... — Numărul de notificări noi de la o dată
        // Folosit de butonul de notificări din header pentru badge-ul cu numărul de alerte noi
        // Default: numără notificările din ultimele 24 de ore
        [HttpGet("unread-count")]
        public async Task<IActionResult> GetUnreadCount([FromQuery] string? since = null)
        {
            var userId = GetCallerId();
            if (userId == null) return Forbid();

            // Parsăm data de la care numărăm (implicit: ultimele 24h)
            var sinceDate = DateTime.UtcNow.AddHours(-24);
            if (!string.IsNullOrEmpty(since) && DateTime.TryParse(since, out var parsed))
                sinceDate = parsed.ToUniversalTime(); // Convertim la UTC pentru comparație cu DB

            var count = await _dbContext.Notifications
                .Where(n => n.IdUser == userId.Value && n.DeletedAt == null && n.CreatedAt > sinceDate)
                .CountAsync();

            return Ok(new { Count = count });
        }
    }
}
