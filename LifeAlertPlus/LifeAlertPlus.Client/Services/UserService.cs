using System.Net.Http.Json;
using LifeAlertPlus.Shared.DTOs.Requests.User;

namespace LifeAlertPlus.Client.Services
{
    public class UserService
    {
        private readonly HttpClient _httpClient;
        public UserService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<bool> UpdateUserAsync(UserUpdateRequestDTO request)
        {
            var response = await _httpClient.PutAsJsonAsync("api/user/update", request);

            return response.IsSuccessStatusCode;
        }
    }
}