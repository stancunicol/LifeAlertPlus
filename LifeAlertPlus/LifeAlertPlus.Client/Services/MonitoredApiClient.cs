using System.Net.Http.Json;
using LifeAlertPlus.Shared.DTOs.Requests.Monitored;
using LifeAlertPlus.Shared.DTOs.Responses.ESP;
using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Client.Services
{
    public class MonitoredApiClient
    {
        private readonly HttpClient _httpClient;

        public MonitoredApiClient(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<bool> AddMonitoredPersonAsync(MonitorAddRequestDTO monitoredPerson)
        {
            var response = await _httpClient.PostAsJsonAsync("api/monitored/add", monitoredPerson);
            return response.IsSuccessStatusCode;
        }

        public async Task<Monitored?> GetMonitoredPersonByIdAsync(Guid id)
        {
            var response = await _httpClient.GetAsync($"api/monitored/id/{id}");

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<Monitored>();
            return result;
        }

        public async Task<ESPDataResponseDTO?> GetEspDataAsync(string deviceSerialNumber, CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.GetAsync($"api/esp/data/{deviceSerialNumber}", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            var dto = await response.Content.ReadFromJsonAsync<ESPDataResponseDTO>(cancellationToken: cancellationToken);
            if (dto != null && string.IsNullOrWhiteSpace(dto.Serial))
                dto.Serial = deviceSerialNumber;

            return dto;
        }

        public async Task<bool> UpdateMonitoredPersonAsync(Guid id, MonitorUpdateRequestDTO dto)
        {
            var response = await _httpClient.PutAsJsonAsync($"api/monitored/update/{id}", dto);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> ArchiveMonitoredPersonAsync(Guid id)
        {
            var response = await _httpClient.PutAsync($"api/monitored/archive/{id}", null);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> RestoreMonitoredPersonAsync(Guid id)
        {
            var response = await _httpClient.PutAsync($"api/monitored/restore/{id}", null);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> DeleteMonitoredPersonAsync(Guid id)
        {
            var response = await _httpClient.DeleteAsync($"api/monitored/{id}");
            return response.IsSuccessStatusCode;
        }

        public async Task<RemoveMonitoredResult?> RemoveMonitoredAsync(Guid id)
        {
            var response = await _httpClient.DeleteAsync($"api/monitored/{id}/remove");
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<RemoveMonitoredResult>();
        }

        public async Task<bool> ReactivateMonitoredAsync(Guid id)
        {
            var response = await _httpClient.PutAsync($"api/monitored/reactivate/{id}", null);
            return response.IsSuccessStatusCode;
        }
    }

    public record RemoveMonitoredResult(bool WasLastOwner, string Message);
}
