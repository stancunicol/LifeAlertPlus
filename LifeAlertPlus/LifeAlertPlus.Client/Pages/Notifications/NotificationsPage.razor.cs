using Microsoft.AspNetCore.Components;
using LifeAlertPlus.Client.Services;
using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Client.Pages.Notifications;

public partial class NotificationsPage : ComponentBase, IAsyncDisposable
{

        [Inject]
        private TokenParserService TokenParserService { get; set; } = default!;

        [Inject]
        private UserService UserService { get; set; } = default!;

        [Inject]
        private NotificationService NotificationService { get; set; } = default!;

        [Inject]
        private PushNotificationClientService PushService { get; set; } = default!;

        [Inject]
        private LanguageService Lang { get; set; } = default!;

        private string T(string key) => Lang.T(key);

        private string UserFullName = "";
        private string ProfilePictureUrl = "";
        private string ActiveFilter = "All";
        private List<Notification> AllNotifications = new();

        private bool _subscribed = false;
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

                // Load notifications
                AllNotifications = await NotificationService.GetRecentNotificationsAsync(50);

                // Subscribe to push notifications for live update
                if (!_subscribed)
                {
                    PushService.OnNotificationReceived += OnPushNotificationReceived;
                    _subscribed = true;
                }
            }
            else
            {
                UserFullName = "User";
            }
        }

        private async void OnPushNotificationReceived(string message, string severity)
        {
            // Reload notifications from API (to get full details)
            AllNotifications = await NotificationService.GetRecentNotificationsAsync(50);
            await InvokeAsync(StateHasChanged);
        }

        public async ValueTask DisposeAsync()
        {
            if (_subscribed)
            {
                PushService.OnNotificationReceived -= OnPushNotificationReceived;
                _subscribed = false;
            }
        }
}