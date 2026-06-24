using System.Net.Http.Json;
using Microsoft.JSInterop;
using LifeAlertPlus.Shared.DTOs.Requests.User;
using LifeAlertPlus.Shared.DTOs.Responses.User;

namespace LifeAlertPlus.Client.Services
{
    // Client HTTP pentru endpoint-urile /api/authentication — login, înregistrare, schimbare
    // email/parolă și logout (curățarea token-ului JWT din sessionStorage)
    public class AuthApiClient
    {
        private readonly HttpClient _httpClient;
        private readonly IJSRuntime _jsRuntime;

        public AuthApiClient(HttpClient httpClient, IJSRuntime jsRuntime)
        {
            _httpClient = httpClient;
            _jsRuntime = jsRuntime;
        }

        // Autentifică utilizatorul; returnează null dacă răspunsul HTTP nu indică succes
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

        // Înregistrează un cont nou; returnează null dacă răspunsul HTTP nu indică succes
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

        // Schimbă adresa de email; la eroare (ex: 409 Conflict pentru email deja folosit)
        // încearcă să extragă mesajul de eroare din body pentru a-l afișa utilizatorului,
        // altfel returnează un mesaj generic de eșec
        public async Task<UserUpdateEmailResponseDTO?> UpdateEmailAsync(UserChangeEmailRequestDTO request)
        {
            var response = await _httpClient.PatchAsJsonAsync("api/authentication/change-email", request);

            if (response.IsSuccessStatusCode)
                return await response.Content.ReadFromJsonAsync<UserUpdateEmailResponseDTO>();

            // Surface error messages (e.g. 409 Conflict for duplicate email)
            try
            {
                var err = await response.Content.ReadFromJsonAsync<UserUpdateEmailResponseDTO>();
                if (err != null) return new UserUpdateEmailResponseDTO { Success = false, Message = err.Message };
            }
            catch { }
            return new UserUpdateEmailResponseDTO { Success = false, Message = "Failed to change email." };
        }

        // Schimbă parola contului
        public async Task<bool> UpdatePasswordAsync(UserChangePasswordRequestDTO request)
        {
            var response = await _httpClient.PostAsJsonAsync("api/authentication/change-password", request);

            return response.IsSuccessStatusCode;
        }

        // Deconectează utilizatorul local — șterge token-ul JWT și URL-ul pozei de profil
        // din sessionStorage (nu există apel către backend pentru logout)
        public async Task LogoutAsync()
        {
            await _jsRuntime.InvokeVoidAsync("sessionStorage.removeItem", "authToken");
            await _jsRuntime.InvokeVoidAsync("sessionStorage.removeItem", "profilePictureUrl");
        }
    }
}
