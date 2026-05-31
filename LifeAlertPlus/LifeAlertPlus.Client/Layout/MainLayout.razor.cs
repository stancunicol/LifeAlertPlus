using Microsoft.AspNetCore.Components;
using LifeAlertPlus.Client.Services;
using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using Microsoft.Extensions.Configuration;

namespace LifeAlertPlus.Client.Layout
{
    public partial class MainLayout : LayoutComponentBase, IAsyncDisposable
    {
        [Inject] private PushNotificationClientService PushService { get; set; } = null!;
        [Inject] private TokenParserService TokenParser { get; set; } = null!;
        [Inject] private IJSRuntime JSRuntime { get; set; } = null!;
        [Inject] private IConfiguration Config { get; set; } = null!;

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

                // Subscribe to Web Push if browser supports it and permission not yet denied
                try
                {
                    var supported  = await JSRuntime.InvokeAsync<bool>("webPushIsSupported");
                    var permission = await JSRuntime.InvokeAsync<string>("webPushGetPermission");
                    if (supported && permission != "denied")
                    {
                        var apiBase = Config["ApiBaseUrl"] ?? "";
                        var token   = await JSRuntime.InvokeAsync<string?>("sessionStorage.getItem", "authToken") ?? "";
                        await JSRuntime.InvokeAsync<bool>("webPushSubscribe", apiBase, token);
                    }
                }
                catch { /* Web Push is best-effort — never block app startup */ }
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