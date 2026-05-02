using System.Net.Http.Json;
using LifeAlertPlus.Shared.DTOs.Requests.Chat;
using LifeAlertPlus.Shared.DTOs.Responses.Chat;

namespace LifeAlertPlus.Client.Services
{
    public class ChatbotClientService
    {
        private readonly HttpClient _http;

        public ChatbotClientService(HttpClient http)
        {
            _http = http;
        }

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
