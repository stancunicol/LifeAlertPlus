using System.Net.Http.Json;
using LifeAlertPlus.Shared.DTOs.Requests.AI;
using LifeAlertPlus.Shared.DTOs.Responses.AI;

namespace LifeAlertPlus.Client.Services
{
    public class AIPredictionService
    {
        private readonly HttpClient _httpClient;

        public AIPredictionService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<AIPredictionResponseDTO?> GetPredictionAsync(AIPredictionRequestDTO request)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("api/ai/predict", request);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<AIPredictionResponseDTO>();
                }
                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
