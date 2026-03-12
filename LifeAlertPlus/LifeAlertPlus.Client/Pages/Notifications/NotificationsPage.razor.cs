using Microsoft.AspNetCore.Components;
using LifeAlertPlus.Client.Services;
using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Client.Pages.Notifications;

public partial class NotificationsPage : ComponentBase
{
        [Inject]
        private TokenParserService TokenParserService { get; set; } = default!;

        private string UserFullName = "";
        private string ProfilePictureUrl = "";
        private string ActiveFilter = "All";
        private List<Notification> AllNotifications;

        protected override async Task OnInitializedAsync()
        {
            var claims = await TokenParserService.GetClaimsAsync();
            if (claims != null)
            {
                UserFullName = $"{claims.FirstName} {claims.LastName}".Trim();
                ProfilePictureUrl = claims.ProfilePictureUrl;
            }
            else
            {
                UserFullName = "User";
            }
        }
}