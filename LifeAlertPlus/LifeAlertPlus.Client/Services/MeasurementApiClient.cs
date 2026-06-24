using System.Net.Http.Json;
using LifeAlertPlus.Shared.DTOs.Responses.Measurement;
using LifeAlertPlus.Shared.DTOs.Requests.Measurement;

namespace LifeAlertPlus.Client.Services
{
    // Client HTTP pentru endpoint-urile /api/measurement — adăugare și interogare măsurători
    // medicale ale persoanelor monitorizate, plus contorul de măsurători din ziua curentă
    public class MeasurementApiClient
    {
        private readonly HttpClient _httpClient;

        // Notifică UI-ul (ex: dashboard, grafice) când o măsurătoare nouă a fost adăugată cu succes,
        // pasând id-ul persoanei monitorizate pentru a permite reîncărcarea datelor relevante
        public event Action<Guid>? OnMeasurementAdded;

        public MeasurementApiClient(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        // Trimite o măsurătoare nouă către backend; aruncă excepție dacă răspunsul nu e succes
        // (EnsureSuccessStatusCode), altfel declanșează OnMeasurementAdded
        public async Task AddMeasurementAsync(MeasurementRequestDTO measurement)
        {
            var response = await _httpClient.PostAsJsonAsync("api/measurement", measurement);
            response.EnsureSuccessStatusCode();
            OnMeasurementAdded?.Invoke(measurement.IdMonitored);
        }

        // Obține măsurătorile unei persoane monitorizate, paginat; returnează listă vidă la eroare
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

        // Obține o singură măsurătoare după id; returnează null dacă nu există sau cererea a eșuat
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

        // Numărul de măsurători înregistrate astăzi (folosit probabil pentru statistici/dashboard);
        // returnează 0 la orice eroare de rețea sau status non-success
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

    // DTO local pentru deserializarea răspunsului contorului de măsurători din ziua curentă
    public class TodayMeasurementsCountResponse
    {
        public int Count { get; set; }
    }
}
