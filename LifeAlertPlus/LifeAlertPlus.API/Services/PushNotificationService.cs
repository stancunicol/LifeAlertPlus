using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace LifeAlertPlus.API.Services
{
    public interface IPushNotificationService
    {
        Task SendPushNotificationAsync(Guid userId, string message, string severity);
    }

    public class PushNotificationService : IPushNotificationService
    {
        private readonly IHubContext<LifeAlertPlus.API.Hubs.NotificationHub> _hubContext;
        public PushNotificationService(IHubContext<LifeAlertPlus.API.Hubs.NotificationHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public async Task SendPushNotificationAsync(Guid userId, string message, string severity)
        {
            // Send to specific user (userId as string group)
            await _hubContext.Clients.Group(userId.ToString()).SendAsync("ReceiveNotification", message, severity);
        }
    }
}