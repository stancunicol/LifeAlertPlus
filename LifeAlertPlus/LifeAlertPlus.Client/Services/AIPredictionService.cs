using System.Net.Http.Json;
using LifeAlertPlus.Shared.DTOs.Requests.AI;
using LifeAlertPlus.Shared.DTOs.Responses.AI;

namespace LifeAlertPlus.Client.Services
{
    // Client HTTP pentru endpoint-ul /api/ai/predict — trimite date de măsurători către modelul AI
    // și returnează predicția (ex: risc de anomalie), sau null dacă serviciul e indisponibil
    public class AIPredictionService
    {
        private readonly HttpClient _httpClient;

        public AIPredictionService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        // Trimite request-ul de predicție către backend AI; la eroare de rețea/deserializare
        // sau status non-success, returnează null ca apelantul să trateze predicția ca indisponibilă
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
            catch (Exception)
            {
                // Network or deserialization failure — caller treats null as unavailable, so swallow.
                return null;
            }
        }
    }
}
