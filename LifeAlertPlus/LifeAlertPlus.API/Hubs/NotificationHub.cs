using Microsoft.AspNetCore.SignalR;

namespace LifeAlertPlus.API.Hubs
{
    public class NotificationHub : Hub
    {
        public async Task AddToGroup(string groupName)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        }
    }
}