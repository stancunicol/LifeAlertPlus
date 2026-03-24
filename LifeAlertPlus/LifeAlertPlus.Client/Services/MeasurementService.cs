using System.Net.Http.Json;
using LifeAlertPlus.Shared.DTOs.Responses.Measurement;

namespace LifeAlertPlus.Client.Services
{
    public class MeasurementService
    {
        private readonly HttpClient _httpClient;

        public MeasurementService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task AddMeasurementAsync(MeasurementResponseDTO measurement)
        {
            var response = await _httpClient.PostAsJsonAsync("api/measurements", measurement);
            response.EnsureSuccessStatusCode();
        }

        public async Task<IEnumerable<MeasurementResponseDTO>> GetMeasurementsByMonitoredIdAsync(Guid idMonitored, int pageNumber, int pageSize)
        {
            var response = await _httpClient.GetAsync($"api/measurements/monitored/{idMonitored}?pageNumber={pageNumber}&pageSize={pageSize}");
            if (response.IsSuccessStatusCode)
            {
                var measurements = await response.Content.ReadFromJsonAsync<IEnumerable<MeasurementResponseDTO>>();
                return measurements ?? Enumerable.Empty<MeasurementResponseDTO>();
            }
            return Enumerable.Empty<MeasurementResponseDTO>();
        }

        public async Task<MeasurementResponseDTO?> GetMeasurementByIdAsync(Guid id)
        {
            var response = await _httpClient.GetAsync($"api/measurements/{id}");
            if (response.IsSuccessStatusCode)
            {
                var measurement = await response.Content.ReadFromJsonAsync<MeasurementResponseDTO>();
                return measurement;
            }
            return null;
        }
    }
}