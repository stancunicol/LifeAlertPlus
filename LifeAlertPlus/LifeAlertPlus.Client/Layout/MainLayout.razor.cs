using Microsoft.AspNetCore.Components;
using LifeAlertPlus.Client.Services;
using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using Microsoft.Extensions.Configuration;

namespace LifeAlertPlus.Client.Layout
{
    // Code-behind pentru layout-ul principal al aplicației — pornește conexiunea de notificări push
    // (SignalR/realtime) și abonarea la Web Push la încărcarea oricărei pagini autentificate
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
            // Pornește serviciul de notificări doar dacă există un utilizator autentificat valid
            var claims = await TokenParser.GetClaimsAsync();
            if (claims?.UserId != Guid.Empty)
            {
                await PushService.StartAsync(claims.UserId);
                PushService.OnNotificationReceived += ShowNotification;
                _connected = true;

                // Se abonează la Web Push dacă browserul suportă funcția și permisiunea nu a fost refuzată
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
                catch { /* Web Push e best-effort — nu trebuie să blocheze niciodată pornirea aplicației */ }
            }
        }

        // Handler apelat când serviciul de push primește o notificare nouă — o afișează ca toast
        private void ShowNotification(string message, string severity)
        {
            ToastRef?.Show(message, severity);
        }

        // Dezabonează handler-ul și eliberează conexiunea de push la distrugerea layout-ului
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