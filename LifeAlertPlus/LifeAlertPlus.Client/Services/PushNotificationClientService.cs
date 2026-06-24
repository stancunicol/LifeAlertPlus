using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
namespace LifeAlertPlus.Client.Services
{
    // Client SignalR pentru hub-ul de notificări push în timp real — se conectează la /notificationhub
    // și expune evenimentul OnNotificationReceived consumat de componentele UI (toast-uri, alerte)
    public class PushNotificationClientService : IAsyncDisposable
    {
        private NavigationManager Navigation { get; set; } = null!;

        private HubConnection? _hubConnection;
        public event Action<string, string>? OnNotificationReceived;

        private readonly string _notificationHubUrl;
        private readonly IJSRuntime _jsRuntime;

        // Construiește URL-ul hub-ului SignalR din configurația ApiBaseUrl (cu fallback la localhost)
        public PushNotificationClientService(IConfiguration config, IJSRuntime jsRuntime)
        {
            var apiBaseUrl = config["ApiBaseUrl"] ?? config["Urls:ApiBaseUrl"] ?? "http://localhost:5176";
            apiBaseUrl = apiBaseUrl.TrimEnd('/');
            _notificationHubUrl = apiBaseUrl + "/notificationhub";
            _jsRuntime = jsRuntime;
        }

        // Pornește conexiunea SignalR (idempotent — nu reconectează dacă există deja o conexiune),
        // atașează token-ul JWT din sessionStorage la fiecare negociere de conexiune,
        // activează reconectarea automată și înrolează utilizatorul în grupul propriu (după userId)
        // ca să primească doar notificările destinate lui
        public async Task StartAsync(Guid userId)
        {
            if (_hubConnection != null)
                return;

            _hubConnection = new HubConnectionBuilder()
                .WithUrl(_notificationHubUrl, options =>
                {
                    options.AccessTokenProvider = async () =>
                    {
                        try { return await _jsRuntime.InvokeAsync<string>("sessionStorage.getItem", "authToken"); }
                        catch { return null; }
                    };
                })
                .WithAutomaticReconnect()
                .Build();

            // Handler invocat de server la fiecare notificare push — propagă mesajul + severitatea către UI
            _hubConnection.On<string, string>("ReceiveNotification", (message, severity) =>
            {
                OnNotificationReceived?.Invoke(message, severity);
            });

            await _hubConnection.StartAsync();
            await _hubConnection.InvokeAsync("AddToGroup", userId.ToString());
        }

        // Eliberează conexiunea SignalR la dispose-ul componentei/serviciului (ex: logout, navigare away)
        public async ValueTask DisposeAsync()
        {
            if (_hubConnection != null)
            {
                await _hubConnection.DisposeAsync();
                _hubConnection = null;
            }
        }
    }
}