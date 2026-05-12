using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace LifeAlertPlus.API.Hubs
{
    [Authorize]
    public class NotificationHub : Hub
    {
        public async Task AddToGroup(string groupName)
        {
            var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId) || !string.Equals(groupName, userId, StringComparison.OrdinalIgnoreCase))
                throw new HubException("Access denied.");

            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        }
    }
}