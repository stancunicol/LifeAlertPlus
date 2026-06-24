using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using LifeAlertPlus.Infrastructure.Context;
using System.Text.Json;
using WebPush;

namespace LifeAlertPlus.API.Services
{
    // Interfața publică — definește contractul pentru trimiterea notificărilor push
    public interface IPushNotificationService
    {
        Task SendPushNotificationAsync(Guid userId, string message, string severity);
    }

    // Implementare care trimite notificări prin două canale simultan:
    // 1. SignalR (în timp real, dacă tab-ul e deschis)
    // 2. Web Push / VAPID (în background, chiar dacă tab-ul e închis sau browser-ul e minimizat)
    public class PushNotificationService : IPushNotificationService
    {
        private readonly IHubContext<LifeAlertPlus.API.Hubs.NotificationHub> _hubContext; // Contextul SignalR pentru trimitere mesaje
        private readonly IServiceScopeFactory _scopeFactory; // Singleton nu poate injecta Scoped direct
        private readonly IConfiguration _config; // Cheile VAPID din configurare
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

        // Trimite o notificare unui utilizator prin ambele canale (SignalR + Web Push)
        // userId: ID-ul utilizatorului destinatar
        // message: textul notificării
        // severity: "Info", "Alert", "Critical" — folosit de client pentru colorare/sunet
        public async Task SendPushNotificationAsync(Guid userId, string message, string severity)
        {
            // 1. SignalR (in-app, real-time)
            // Trimitem direct la grupul utilizatorului (toate tab-urile deschise ale lui)
            await _hubContext.Clients.Group(userId.ToString()).SendAsync("ReceiveNotification", message, severity);

            // 2. Web Push (background, even when tab is closed)
            // Citim cheile VAPID necesare pentru autentificarea cu serviciile push ale browser-elor
            var publicKey  = _config["WebPush:VapidPublicKey"];
            var privateKey = _config["WebPush:VapidPrivateKey"];
            var subject    = _config["WebPush:VapidSubject"] ?? "mailto:support@lifealertplusiot.com"; // Contact obligatoriu VAPID

            // Dacă cheile VAPID lipsesc, nu putem trimite Web Push — ieșim silențios
            if (string.IsNullOrEmpty(publicKey) || string.IsNullOrEmpty(privateKey))
                return;

            // Citim toate subscripțiile Web Push ale utilizatorului din DB
            // (un utilizator poate fi abonat pe mai multe dispozitive/browsere)
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LifeAlertPlusDbContext>();
            var subs = await db.PushSubscriptions
                .Where(p => p.UserId == userId)
                .ToListAsync();

            if (subs.Count == 0) return; // Fără subscripții, nu există unde să trimitem

            var vapidDetails = new VapidDetails(subject, publicKey, privateKey); // Credenâialele VAPID
            var client       = new WebPushClient(); // Client-ul librăriei WebPush
            // Construim payload-ul JSON al notificării (titlu, body, severitate)
            var payload      = JsonSerializer.Serialize(new
            {
                title    = "LifeAlertPlus",
                body     = message,
                severity = severity // Folosit de Service Worker pentru a decide comportamentul
            });

            // Trimitem notificarea la fiecare subscripție (fiecare browser/dispozitiv)
            foreach (var sub in subs)
            {
                try
                {
                    // PushSubscription conține endpoint-ul browserului + cheile de criptare
                    var pushSub = new PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
                    await client.SendNotificationAsync(pushSub, payload, vapidDetails);
                }
                catch (WebPushException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Gone
                                                || ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Subscription expired — remove it
                    // 410 Gone / 404 Not Found = subscripția a expirat sau utilizatorul a dezabonat browserul
                    // Ștergem subscripția din DB pentru a nu mai consuma resurse
                    db.PushSubscriptions.Remove(sub);
                    _logger.LogInformation("Removed expired push subscription for user {UserId}", userId);
                }
                catch (Exception ex)
                {
                    // Alte erori (rețea, server push indisponibil) — logăm și continuăm cu restul subscripțiilor
                    _logger.LogWarning(ex, "Web Push failed for user {UserId}", userId);
                }
            }

            // Salvăm ștergerea subscripțiilor expirate
            await db.SaveChangesAsync();
        }
    }
}
