using System.Net.Http.Json;
using LifeAlertPlus.Shared.DTOs.Requests.User;
using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Client.Services
{
    public class UserService
    {
        private readonly HttpClient _httpClient;
        public UserService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<bool> UpdateUserAsync(Guid userId, UserUpdateRequestDTO request)
        {
            var response = await _httpClient.PutAsJsonAsync($"api/user/update/{userId}", request);

            return response.IsSuccessStatusCode;
        }

        public async Task<bool> DeactivateUserAsync(Guid userId)
        {
            var response = await _httpClient.PatchAsync($"api/user/deactivate/{userId}", null);

            return response.IsSuccessStatusCode;
        }

        public async Task<bool> ActivateUserAsync(Guid userId)
        {
            var response = await _httpClient.PatchAsync($"api/user/activate/{userId}", null);

            return response.IsSuccessStatusCode;
        }

        public async Task<bool> DeleteUserAsync(Guid userId)
        {
            var response = await _httpClient.DeleteAsync($"api/user/delete/{userId}");

            return response.IsSuccessStatusCode;
        }

        public class UploadResult
        {
            public string? ImageUrl { get; set; }
        }

        public async Task<string?> UploadProfilePictureAsync(Guid userId, Stream imageStream, string fileName)
        {
            var content = new MultipartFormDataContent();
            var imageContent = new StreamContent(imageStream);
            imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            content.Add(imageContent, "file", fileName);

            var response = await _httpClient.PostAsync($"api/user/upload-profile-image/{userId}", content);
            if (!response.IsSuccessStatusCode)
                return null;

            var result = await response.Content.ReadFromJsonAsync<UploadResult>();
            return result?.ImageUrl;
        }

        public async Task<User?> GetUserByIdAsync(Guid userId)
        {
            var response = await _httpClient.GetAsync($"api/user/{userId}");
            if (!response.IsSuccessStatusCode)
                return null;

            var user = await response.Content.ReadFromJsonAsync<User>();
            return user;
        }
    }
}