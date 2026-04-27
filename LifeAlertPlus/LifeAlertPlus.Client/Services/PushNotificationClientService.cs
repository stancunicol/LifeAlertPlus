using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Components;
namespace LifeAlertPlus.Client.Services
{
    public class PushNotificationClientService : IAsyncDisposable
    {
        private NavigationManager Navigation { get; set; } = null!;

        private HubConnection? _hubConnection;
        public event Action<string, string>? OnNotificationReceived;

        private readonly string _notificationHubUrl;

        public PushNotificationClientService(IConfiguration config)
        {
            var apiBaseUrl = config["ApiBaseUrl"] ?? config["Urls:ApiBaseUrl"] ?? "http://localhost:5176";
            apiBaseUrl = apiBaseUrl.TrimEnd('/');
            _notificationHubUrl = apiBaseUrl + "/notificationhub";
        }

        public async Task StartAsync(Guid userId)
        {
            if (_hubConnection != null)
                return;

            _hubConnection = new HubConnectionBuilder()
                .WithUrl(_notificationHubUrl)
                .WithAutomaticReconnect()
                .Build();

            _hubConnection.On<string, string>("ReceiveNotification", (message, severity) =>
            {
                OnNotificationReceived?.Invoke(message, severity);
            });

            await _hubConnection.StartAsync();
            await _hubConnection.InvokeAsync("AddToGroup", userId.ToString());
        }

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