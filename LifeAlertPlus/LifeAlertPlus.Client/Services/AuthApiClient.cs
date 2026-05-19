using System.Net.Http.Json;
using Microsoft.JSInterop;
using LifeAlertPlus.Shared.DTOs.Requests.User;
using LifeAlertPlus.Shared.DTOs.Responses.User;

namespace LifeAlertPlus.Client.Services
{
    public class AuthApiClient
    {
        private readonly HttpClient _httpClient;
        private readonly IJSRuntime _jsRuntime;

        public AuthApiClient(HttpClient httpClient, IJSRuntime jsRuntime)
        {
            _httpClient = httpClient;
            _jsRuntime = jsRuntime;
        }

        public async Task<UserLoginResponseDTO?> LoginAsync(UserLoginRequestDTO request)
        {
            var response = await _httpClient.PostAsJsonAsync("api/authentication/login", request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<UserLoginResponseDTO>();
                return result;
            }
            return null;
        }

        public async Task<UserRegisterResponseDTO?> RegisterAsync(UserRegisterRequestDTO request)
        {
            var response = await _httpClient.PostAsJsonAsync("api/authentication/register", request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<UserRegisterResponseDTO>();
                return result;
            }
            return null;
        }

        public async Task<UserUpdateEmailResponseDTO?> UpdateEmailAsync(UserChangeEmailRequestDTO request)
        {
            var response = await _httpClient.PatchAsJsonAsync("api/authentication/change-email", request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<UserUpdateEmailResponseDTO>();
                return result;
            }
            return null;
        }

        public async Task<bool> UpdatePasswordAsync(UserChangePasswordRequestDTO request)
        {
            var response = await _httpClient.PostAsJsonAsync("api/authentication/change-password", request);

            return response.IsSuccessStatusCode;
        }

        public async Task LogoutAsync()
        {
            await _jsRuntime.InvokeVoidAsync("sessionStorage.removeItem", "authToken");
            await _jsRuntime.InvokeVoidAsync("sessionStorage.removeItem", "profilePictureUrl");
        }
    }
}
