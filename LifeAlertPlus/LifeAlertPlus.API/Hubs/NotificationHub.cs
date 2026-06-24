using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace LifeAlertPlus.API.Hubs
{
    // Hub SignalR pentru notificări în timp real (conexiune WebSocket persistentă între server și browser)
    // Marcăm cu [Authorize] — doar utilizatorii autentificați cu JWT valid pot conecta
    [Authorize]
    public class NotificationHub : Hub
    {
        // Metodă apelată de client pentru a se alătura unui grup de notificări personal
        // groupName este ID-ul utilizatorului — fiecare user are propriul grup de mesaje
        public async Task AddToGroup(string groupName)
        {
            // Extragem ID-ul utilizatorului autentificat din claim-urile JWT
            var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            // Securitate: utilizatorul poate intra DOAR în propriul său grup (nu în al altora)
            // Verificăm că groupName coincide exact cu ID-ul utilizatorului autentificat
            if (string.IsNullOrEmpty(userId) || !string.Equals(groupName, userId, StringComparison.OrdinalIgnoreCase))
                throw new HubException("Access denied."); // HubException e trimisă clientului ca eroare SignalR

            // Adăugăm conexiunea curentă în grupul utilizatorului
            // Context.ConnectionId = identificatorul unic al acestei conexiuni WebSocket
            // Un utilizator poate avea mai multe conexiuni (tab-uri diferite) — toate merg în același grup
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        }
    }
}
