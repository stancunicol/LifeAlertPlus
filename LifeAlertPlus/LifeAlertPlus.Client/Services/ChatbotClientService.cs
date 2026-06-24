using System.Net.Http.Json;
using LifeAlertPlus.Shared.DTOs.Requests.Chat;
using LifeAlertPlus.Shared.DTOs.Responses.Chat;

namespace LifeAlertPlus.Client.Services
{
    // Client HTTP pentru endpoint-ul /api/chat — trimite istoricul conversației către chatbot
    // și returnează răspunsul generat, sau null la eroare/indisponibilitate
    public class ChatbotClientService
    {
        private readonly HttpClient _http;

        public ChatbotClientService(HttpClient http)
        {
            _http = http;
        }

        // Trimite mesajele conversației + limba curentă către backend și extrage textul răspunsului;
        // orice eroare (rețea, deserializare, status non-success) e înghițită și returnează null
        // ca UI-ul să poată afișa un mesaj de fallback fără să crape
        public async Task<string?> SendAsync(List<ChatMessageDTO> messages, string lang)
        {
            try
            {
                var request = new ChatRequest { Messages = messages, Lang = lang };
                var response = await _http.PostAsJsonAsync("api/chat", request);
                if (!response.IsSuccessStatusCode) return null;
                var result = await response.Content.ReadFromJsonAsync<ChatResponse>();
                return result?.Reply;
            }
            catch
            {
                return null;
            }
        }
    }
}
