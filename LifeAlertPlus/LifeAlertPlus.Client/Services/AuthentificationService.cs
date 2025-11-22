using System.Net.Http.Json;
using LifeAlertPlus.Shared.DTOs.Requests.User;
using LifeAlertPlus.Shared.DTOs.Responses.User;

namespace LifeAlertPlus.Client.Services
{
    public class AuthentificationService
    {
        private readonly HttpClient _httpClient;
        public AuthentificationService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<UserLoginResponseDTO?> LoginAsync(UserLoginRequestDTO request)
        {
            var response = await _httpClient.PostAsJsonAsync("api/authentification/login", request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<UserLoginResponseDTO>();
                return result;
            }
            return null;
        }

        public async Task<UserRegisterRequestDTO?> RegisterAsync(UserRegisterRequestDTO request)
        {
            var response = await _httpClient.PostAsJsonAsync("api/authentification/register", request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<UserRegisterRequestDTO>();
                return result;
            }
            return null;
        }
    }
}
