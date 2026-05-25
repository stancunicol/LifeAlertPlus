using System.Net.Http;
using System.Net.Http.Json;
using LifeAlertPlus.Shared.DTOs.Requests.Wifi;
using LifeAlertPlus.Shared.DTOs.Responses.Wifi;

namespace LifeAlertPlus.Client.Services
{
    public class WifiApiClient
    {
        private readonly HttpClient _httpClient;

        public WifiApiClient(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<List<WifiNetworkResponseDTO>> GetByMonitoredAsync(Guid monitoredId)
        {
            try
            {
                var result = await _httpClient.GetFromJsonAsync<List<WifiNetworkResponseDTO>>($"api/wifi/monitored/{monitoredId}");
                return result ?? new List<WifiNetworkResponseDTO>();
            }
            catch
            {
                return new List<WifiNetworkResponseDTO>();
            }
        }

        public async Task<(bool Success, string? ErrorKey, WifiNetworkResponseDTO? Network)> AddAsync(Guid monitoredId, string ssid, string password)
        {
            var request = new WifiNetworkRequestDTO
            {
                IdMonitored = monitoredId,
                Ssid = ssid,
                Password = password
            };
            var response = await _httpClient.PostAsJsonAsync("api/wifi", request);
            if (response.IsSuccessStatusCode)
            {
                var network = await response.Content.ReadFromJsonAsync<WifiNetworkResponseDTO>();
                return (true, null, network);
            }

            try
            {
                var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
                return (false, error?.Error, null);
            }
            catch
            {
                return (false, null, null);
            }
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var response = await _httpClient.DeleteAsync($"api/wifi/{id}");
            return response.IsSuccessStatusCode;
        }

        private class ErrorResponse
        {
            public string? Error { get; set; }
        }
    }
}
