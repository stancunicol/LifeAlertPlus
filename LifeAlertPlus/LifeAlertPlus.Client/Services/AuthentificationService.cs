using System.Net.Http.Json;
using Microsoft.JSInterop;
using LifeAlertPlus.Shared.DTOs.Requests.User;
using LifeAlertPlus.Shared.DTOs.Responses.User;

namespace LifeAlertPlus.Client.Services
{
    public class AuthentificationService
    {
        private readonly HttpClient _httpClient;
        private readonly IJSRuntime _jsRuntime;
        
        public AuthentificationService(HttpClient httpClient, IJSRuntime jsRuntime)
        {
            _httpClient = httpClient;
            _jsRuntime = jsRuntime;
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

        public async Task<UserUpdateEmailResponseDTO?> UpdateEmailAsync(UserChangeEmailRequestDTO request)
        {
            var response = await _httpClient.PatchAsJsonAsync("api/authentification/change-email", request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<UserUpdateEmailResponseDTO>();
                return result;
            }
            return null;
        }
        
        public async Task LogoutAsync()
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "authToken");
        }
    }
}
