using System.Net.Http.Json;
using LifeAlertPlus.Shared.DTOs.Requests.Monitored;
using LifeAlertPlus.Shared.DTOs.Responses.ESP;
using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Client.Services
{
    public class MonitoredService
    {
        private readonly HttpClient _httpClient;

        public MonitoredService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<bool> AddMonitoredPersonAsync(MonitorAddRequestDTO monitoredPerson)
        {
            var response = await _httpClient.PostAsJsonAsync("api/monitored/add", monitoredPerson);

            return response.IsSuccessStatusCode;
        }

        public async Task<Monitored?> GetMonitoredPersonByDeviceSerialNumberAsync(string deviceSerialNumber)
        {
            var response = await _httpClient.GetAsync($"api/monitored/serial/{deviceSerialNumber}");

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<Monitored>();
            return result;
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
            {
                return null;
            }

            var data = await response.Content.ReadFromJsonAsync<ESPDataResponseDTO>(cancellationToken: cancellationToken);
            return data;
        }
    }
}