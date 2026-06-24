using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Threading.Tasks;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Shared.DTOs.Responses.UserMonitored;

namespace LifeAlertPlus.Client.Services
{
    // Client HTTP pentru endpoint-urile /api/usermonitored — gestionează relația many-to-many
    // dintre utilizatori (supraveghetori) și persoanele monitorizate (asociere, listare, arhivă)
    public class UserMonitoredApiClient
    {
        private readonly HttpClient _httpClient;

        public UserMonitoredApiClient(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        // Asociază o persoană monitorizată existentă cu un utilizator (supraveghetor)
        public async Task<bool> AddMonitoredPersonToUserAsync(Guid userId, Guid monitoredPersonId)
        {
            var response = await _httpClient.PostAsync($"api/usermonitored/{userId}/monitored/{monitoredPersonId}", null);

            return response.IsSuccessStatusCode;
        }

        // Listează persoanele monitorizate de un utilizator; includeArchived controlează dacă
        // se includ și persoanele arhivate. Returnează listă vidă la eroare HTTP sau body gol
        public async Task<IReadOnlyList<Monitored>> GetMonitoredPeopleAsync(Guid userId, bool includeArchived = false)
        {
            var query = includeArchived ? "?includeArchived=true" : string.Empty;
            var response = await _httpClient.GetAsync($"api/usermonitored/{userId}/monitored{query}");

            if (!response.IsSuccessStatusCode)
            {
                return Array.Empty<Monitored>();
            }

            var result = await response.Content.ReadFromJsonAsync<List<Monitored>>();
            return result == null ? Array.Empty<Monitored>() : result;
        }

        // Listează doar persoanele monitorizate arhivate ale unui utilizator
        public async Task<IReadOnlyList<Monitored>> GetArchivedMonitoredPeopleAsync(Guid userId)
        {
            var response = await _httpClient.GetAsync($"api/usermonitored/{userId}/monitored/archived");

            if (!response.IsSuccessStatusCode)
            {
                return Array.Empty<Monitored>();
            }

            var result = await response.Content.ReadFromJsonAsync<List<Monitored>>();
            return result == null ? Array.Empty<Monitored>() : result;
        }

        // Listează toate asocierile utilizator-persoană monitorizată din sistem (probabil uz admin)
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
