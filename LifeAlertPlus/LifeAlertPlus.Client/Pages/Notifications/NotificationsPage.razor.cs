using Microsoft.AspNetCore.Components;
using LifeAlertPlus.Client.Services;
using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Client.Pages.Notifications;

public partial class NotificationsPage : ComponentBase
{
        [Inject]
        private TokenParserService TokenParserService { get; set; } = default!;

        [Inject]
        private UserService UserService { get; set; } = default!;

        private string UserFullName = "";
        private string ProfilePictureUrl = "";
        private string ActiveFilter = "All";
        private List<Notification> AllNotifications = new();

        protected override async Task OnInitializedAsync()
        {
            var claims = await TokenParserService.GetClaimsAsync();
            if (claims != null)
            {
                UserFullName = $"{claims.FirstName} {claims.LastName}".Trim();
                ProfilePictureUrl = claims.ProfilePictureUrl;

                var userProfile = await UserService.GetUserByIdAsync(claims.UserId);
                if (userProfile != null)
                {
                    var apiName = $"{userProfile.FirstName} {userProfile.LastName}".Trim();
                    if (!string.IsNullOrWhiteSpace(apiName))
                        UserFullName = apiName;
                    if (!string.IsNullOrWhiteSpace(userProfile.ProfilePictureUrl))
                        ProfilePictureUrl = userProfile.ProfilePictureUrl;
                }
            }
            else
            {
                UserFullName = "User";
            }
        }
}