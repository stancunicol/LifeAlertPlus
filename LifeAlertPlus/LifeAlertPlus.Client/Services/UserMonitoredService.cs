using System.Net.Http.Json;
using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Client.Services
{
    public class UserMonitoredService
    {
        private readonly HttpClient _httpClient;

        public UserMonitoredService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<bool> AddMonitoredPersonToUserAsync(Guid userId, Guid monitoredPersonId)
        {
            var response = await _httpClient.PostAsync($"api/usermonitored/{userId}/monitored/{monitoredPersonId}", null);

            return response.IsSuccessStatusCode;
        }

        public async Task<IReadOnlyList<Monitored>> GetMonitoredPeopleAsync(Guid userId)
        {
            var response = await _httpClient.GetAsync($"api/usermonitored/{userId}/monitored");

            if (!response.IsSuccessStatusCode)
            {
                return Array.Empty<Monitored>();
            }

            var result = await response.Content.ReadFromJsonAsync<List<Monitored>>();
            return result == null ? Array.Empty<Monitored>() : result;
        }
    }
}