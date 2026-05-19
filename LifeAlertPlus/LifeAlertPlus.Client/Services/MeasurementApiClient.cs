using System.Net.Http.Json;
using LifeAlertPlus.Shared.DTOs.Responses.Measurement;
using LifeAlertPlus.Shared.DTOs.Requests.Measurement;

namespace LifeAlertPlus.Client.Services
{
    public class MeasurementApiClient
    {
        private readonly HttpClient _httpClient;

        public MeasurementApiClient(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task AddMeasurementAsync(MeasurementRequestDTO measurement)
        {
            var response = await _httpClient.PostAsJsonAsync("api/measurement", measurement);
            response.EnsureSuccessStatusCode();
        }

        public async Task<IEnumerable<MeasurementResponseDTO>> GetMeasurementsByMonitoredIdAsync(Guid idMonitored, int pageNumber, int pageSize)
        {
            var response = await _httpClient.GetAsync($"api/measurement/monitored/{idMonitored}?pageNumber={pageNumber}&pageSize={pageSize}");
            if (response.IsSuccessStatusCode)
            {
                var measurements = await response.Content.ReadFromJsonAsync<IEnumerable<MeasurementResponseDTO>>();
                return measurements ?? Enumerable.Empty<MeasurementResponseDTO>();
            }
            return Enumerable.Empty<MeasurementResponseDTO>();
        }

        public async Task<MeasurementResponseDTO?> GetMeasurementByIdAsync(Guid id)
        {
            var response = await _httpClient.GetAsync($"api/measurement/{id}");
            if (response.IsSuccessStatusCode)
            {
                var measurement = await response.Content.ReadFromJsonAsync<MeasurementResponseDTO>();
                return measurement;
            }
            return null;
        }

        public async Task<int> GetTodayMeasurementsCountAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("api/measurement/today/count");
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<TodayMeasurementsCountResponse>();
                    return result?.Count ?? 0;
                }
                return 0;
            }
            catch
            {
                return 0;
            }
        }
    }

    public class TodayMeasurementsCountResponse
    {
        public int Count { get; set; }
    }
}
