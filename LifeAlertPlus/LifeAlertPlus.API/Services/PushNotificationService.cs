using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using LifeAlertPlus.Infrastructure.Context;
using System.Text.Json;
using WebPush;

namespace LifeAlertPlus.API.Services
{
    public interface IPushNotificationService
    {
        Task SendPushNotificationAsync(Guid userId, string message, string severity);
    }

    public class PushNotificationService : IPushNotificationService
    {
        private readonly IHubContext<LifeAlertPlus.API.Hubs.NotificationHub> _hubContext;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<PushNotificationService> _logger;

        public PushNotificationService(
            IHubContext<LifeAlertPlus.API.Hubs.NotificationHub> hubContext,
            IServiceScopeFactory scopeFactory,
            IConfiguration config,
            ILogger<PushNotificationService> logger)
        {
            _hubContext   = hubContext;
            _scopeFactory = scopeFactory;
            _config       = config;
            _logger       = logger;
        }

        public async Task SendPushNotificationAsync(Guid userId, string message, string severity)
        {
            // 1. SignalR (in-app, real-time)
            await _hubContext.Clients.Group(userId.ToString()).SendAsync("ReceiveNotification", message, severity);

            // 2. Web Push (background, even when tab is closed)
            var publicKey  = _config["WebPush:VapidPublicKey"];
            var privateKey = _config["WebPush:VapidPrivateKey"];
            var subject    = _config["WebPush:VapidSubject"] ?? "mailto:support@lifealertplusiot.com";

            if (string.IsNullOrEmpty(publicKey) || string.IsNullOrEmpty(privateKey))
                return;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LifeAlertPlusDbContext>();
            var subs = await db.PushSubscriptions
                .Where(p => p.UserId == userId)
                .ToListAsync();

            if (subs.Count == 0) return;

            var vapidDetails = new VapidDetails(subject, publicKey, privateKey);
            var client       = new WebPushClient();
            var payload      = JsonSerializer.Serialize(new
            {
                title    = "LifeAlertPlus",
                body     = message,
                severity = severity
            });

            foreach (var sub in subs)
            {
                try
                {
                    var pushSub = new PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
                    await client.SendNotificationAsync(pushSub, payload, vapidDetails);
                }
                catch (WebPushException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Gone
                                                || ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Subscription expired — remove it
                    db.PushSubscriptions.Remove(sub);
                    _logger.LogInformation("Removed expired push subscription for user {UserId}", userId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Web Push failed for user {UserId}", userId);
                }
            }

            await db.SaveChangesAsync();
        }
    }
}