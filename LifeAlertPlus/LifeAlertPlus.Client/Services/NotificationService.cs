using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Client.Services
{
    public class NotificationService
    {
        private readonly HttpClient _httpClient;
        public NotificationService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<List<Notification>> GetRecentNotificationsAsync(int count = 20)
        {
            var result = await _httpClient.GetFromJsonAsync<List<Notification>>("api/notification/recent?count=" + count);
            return result ?? new List<Notification>();
        }
    }
}