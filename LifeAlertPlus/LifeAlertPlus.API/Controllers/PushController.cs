using LifeAlertPlus.Infrastructure.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/push")]
    public class PushController(LifeAlertPlusDbContext db, ILogger<PushController> logger)
        : BaseApiController
    {
        public record SubscribeRequest(string Endpoint, string P256dh, string Auth);

        [HttpPost("subscribe")]
        public async Task<IActionResult> Subscribe([FromBody] SubscribeRequest req)
        {
            var userId = GetCallerId();
            if (userId == null) return Unauthorized();

            var existing = await db.PushSubscriptions
                .FirstOrDefaultAsync(p => p.Endpoint == req.Endpoint);

            if (existing != null)
            {
                existing.P256dh = req.P256dh;
                existing.Auth   = req.Auth;
                existing.UserId = userId.Value;
            }
            else
            {
                db.PushSubscriptions.Add(new Domain.Entities.PushSubscription
                {
                    Id        = Guid.NewGuid(),
                    UserId    = userId.Value,
                    Endpoint  = req.Endpoint,
                    P256dh    = req.P256dh,
                    Auth      = req.Auth,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await db.SaveChangesAsync();
            logger.LogInformation("Push subscription saved for user {UserId}", userId);
            return Ok();
        }

        [HttpDelete("subscribe")]
        public async Task<IActionResult> Unsubscribe([FromBody] SubscribeRequest req)
        {
            var userId = GetCallerId();
            if (userId == null) return Unauthorized();

            var sub = await db.PushSubscriptions
                .FirstOrDefaultAsync(p => p.Endpoint == req.Endpoint && p.UserId == userId.Value);

            if (sub != null)
            {
                db.PushSubscriptions.Remove(sub);
                await db.SaveChangesAsync();
            }

            return Ok();
        }

        [HttpGet("vapid-public-key")]
        [AllowAnonymous]
        public IActionResult GetVapidPublicKey([FromServices] IConfiguration config)
        {
            var key = config["WebPush:VapidPublicKey"];
            if (string.IsNullOrEmpty(key)) return NotFound();
            return Ok(new { publicKey = key });
        }
    }
}
