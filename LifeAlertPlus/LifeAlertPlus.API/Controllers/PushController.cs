using LifeAlertPlus.Infrastructure.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LifeAlertPlus.API.Controllers
{
    // Controller pentru gestionarea subscripțiilor Web Push VAPID.
    // Browserul se abonează la notificări push prin Service Worker, care primește un endpoint
    // unic de la push service-ul browserului (Chrome, Firefox, Safari).
    // Aceste endpoint-uri sunt stocate în DB și folosite de PushNotificationService pentru livrare.
    [ApiController]
    [Authorize] // Toate operațiunile necesită autentificare
    [Route("api/push")]
    public class PushController(LifeAlertPlusDbContext db, ILogger<PushController> logger)
        : BaseApiController
    {
        // DTO pentru cererea de abonare/dezabonare — conține informațiile push subscription
        // Endpoint: URL-ul unic al push service-ului pentru acest browser
        // P256dh: cheia publică Diffie-Hellman pentru criptarea payload-ului
        // Auth: secretul de autentificare pentru criptare
        public record SubscribeRequest(string Endpoint, string P256dh, string Auth);

        // POST /api/push/subscribe — Înregistrează sau actualizează o subscripție push
        // Apelat de Service Worker-ul Blazor la fiecare pornire a browserului
        [HttpPost("subscribe")]
        public async Task<IActionResult> Subscribe([FromBody] SubscribeRequest req)
        {
            var userId = GetCallerId(); // ID-ul utilizatorului din JWT
            if (userId == null) return Unauthorized();

            try
            {
                // Verificăm dacă browserul e deja abonat (identificat prin Endpoint unic)
                var existing = await db.PushSubscriptions
                    .FirstOrDefaultAsync(p => p.Endpoint == req.Endpoint);

                if (existing != null)
                {
                    // Browserul a reînnoit cheile push — actualizăm subscripția existentă
                    existing.P256dh = req.P256dh;
                    existing.Auth   = req.Auth;
                    existing.UserId = userId.Value; // Poate fi alt utilizator pe același browser
                }
                else
                {
                    // Subscripție nouă — salvăm endpoint-ul și cheile de criptare
                    db.PushSubscriptions.Add(new Domain.Entities.PushSubscription
                    {
                        Id        = Guid.NewGuid(),
                        UserId    = userId.Value,
                        Endpoint  = req.Endpoint,  // URL-ul push service-ului (Chrome/Firefox/Safari)
                        P256dh    = req.P256dh,     // Cheie publică pentru criptare VAPID
                        Auth      = req.Auth,       // Secret de autentificare
                        CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
                    });
                }

                await db.SaveChangesAsync();
                logger.LogInformation("Push subscription saved for user {UserId}", userId);
                return Ok();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to save push subscription for user {UserId}", userId);
                return StatusCode(500, new { Message = ex.Message, Detail = ex.InnerException?.Message });
            }
        }

        // DELETE /api/push/subscribe — Dezabonează un browser de la notificările push
        // Apelat când utilizatorul revocă permisiunea de notificări în browser
        [HttpDelete("subscribe")]
        public async Task<IActionResult> Unsubscribe([FromBody] SubscribeRequest req)
        {
            var userId = GetCallerId();
            if (userId == null) return Unauthorized();

            // Găsim subscripția după Endpoint (unic per browser) și userId (securitate)
            var sub = await db.PushSubscriptions
                .FirstOrDefaultAsync(p => p.Endpoint == req.Endpoint && p.UserId == userId.Value);

            if (sub != null)
            {
                db.PushSubscriptions.Remove(sub); // Ștergem subscripția din DB
                await db.SaveChangesAsync();
            }

            return Ok(); // Returnăm 200 chiar dacă subscripția nu exista (idempotent)
        }

        // GET /api/push/vapid-public-key — Returnează cheia publică VAPID pentru Service Worker
        // Browserul are nevoie de ea pentru a cripta subscripția push la înregistrare
        [HttpGet("vapid-public-key")]
        [AllowAnonymous] // Cheia publică VAPID nu e secretă — poate fi accesată înainte de login
        public IActionResult GetVapidPublicKey([FromServices] IConfiguration config)
        {
            var key = config["WebPush:VapidPublicKey"]; // Citim cheia din appsettings
            if (string.IsNullOrEmpty(key)) return NotFound(); // Nu e configurată
            return Ok(new { publicKey = key }); // Service Worker-ul o va folosi la `pushManager.subscribe()`
        }
    }
}
