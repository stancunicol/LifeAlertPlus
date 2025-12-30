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
    }
}