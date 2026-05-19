using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Threading.Tasks;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Shared.DTOs.Responses.UserMonitored;

namespace LifeAlertPlus.Client.Services
{
    public class UserMonitoredApiClient
    {
        private readonly HttpClient _httpClient;

        public UserMonitoredApiClient(HttpClient httpClient)
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

        public async Task<IReadOnlyList<MonitoredUserDTO>> GetAllMonitoredUsersAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("api/usermonitored");
                if (!response.IsSuccessStatusCode)
                    return Array.Empty<MonitoredUserDTO>();

                var result = await response.Content.ReadFromJsonAsync<List<MonitoredUserDTO>>();
                return result ?? new List<MonitoredUserDTO>();
            }
            catch
            {
                return Array.Empty<MonitoredUserDTO>();
            }
        }
    }
}
