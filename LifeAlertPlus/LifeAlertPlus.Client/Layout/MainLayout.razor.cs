using Microsoft.AspNetCore.Components;
using LifeAlertPlus.Client.Services;
using LifeAlertPlus.Shared.DTOs.Responses.User;
using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace LifeAlertPlus.Client.Layout
{
    public partial class MainLayout : LayoutComponentBase, IAsyncDisposable
    {
        [Inject] private PushNotificationClientService PushService { get; set; } = null!;
        [Inject] private TokenParserService TokenParser { get; set; } = null!;
        [Inject] private IJSRuntime JSRuntime { get; set; } = null!;


        private bool _connected;
        private Components.ToastNotification? ToastRef;

        protected override async Task OnInitializedAsync()
        {
            var claims = await TokenParser.GetClaimsAsync();
            if (claims?.UserId != Guid.Empty)
            {
                await PushService.StartAsync(claims.UserId);
                PushService.OnNotificationReceived += ShowNotification;
                _connected = true;
            }
        }

        private void ShowNotification(string message, string severity)
        {
            ToastRef?.Show(message, severity);
        }

        public async ValueTask DisposeAsync()
        {
            if (_connected)
            {
                PushService.OnNotificationReceived -= ShowNotification;
                await PushService.DisposeAsync();
            }
        }
    }
}