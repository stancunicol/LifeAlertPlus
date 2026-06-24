using System;
using System.Net.Http.Json;
using LifeAlertPlus.Shared.DTOs.Requests.User;
using LifeAlertPlus.Shared.DTOs.Responses.User;

namespace LifeAlertPlus.Client.Services
{
    // Client HTTP pentru endpoint-urile /api/user — CRUD utilizatori, activare/dezactivare/ștergere,
    // export GDPR, upload poză de profil
    public class UserApiClient
    {
        private readonly HttpClient _httpClient;
        public UserApiClient(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        // Listează toți utilizatorii (probabil uz admin); returnează listă vidă la eroare
        public async Task<IReadOnlyList<UserListItemDTO>> GetAllUsersAsync()
        {
            try
            {
                var users = await _httpClient.GetFromJsonAsync<List<UserListItemDTO>>("api/user");
                return users ?? new List<UserListItemDTO>();
            }
            catch
            {
                return Array.Empty<UserListItemDTO>();
            }
        }

        // Actualizează datele profilului unui utilizator
        public async Task<bool> UpdateUserAsync(Guid userId, UserUpdateRequestDTO request)
        {
            var response = await _httpClient.PutAsJsonAsync($"api/user/update/{userId}", request);

            return response.IsSuccessStatusCode;
        }

        // Dezactivează contul (soft — utilizatorul nu se mai poate autentifica, dar datele rămân)
        public async Task<bool> DeactivateUserAsync(Guid userId)
        {
            var response = await _httpClient.PatchAsync($"api/user/deactivate/{userId}", null);

            return response.IsSuccessStatusCode;
        }

        // Reactivează un cont dezactivat anterior
        public async Task<bool> ActivateUserAsync(Guid userId)
        {
            var response = await _httpClient.PatchAsync($"api/user/activate/{userId}", null);

            return response.IsSuccessStatusCode;
        }

        // Șterge definitiv contul unui utilizator
        public async Task<bool> DeleteUserAsync(Guid userId)
        {
            var response = await _httpClient.DeleteAsync($"api/user/delete/{userId}");
            return response.IsSuccessStatusCode;
        }

        // Descarcă exportul GDPR (toate datele personale ale utilizatorului) ca fișier binar;
        // numele fișierului e extras din header-ul Content-Disposition al răspunsului,
        // cu fallback la un nume generat din data curentă
        public async Task<(byte[]? Data, string FileName)> GdprExportAsync(Guid userId)
        {
            var response = await _httpClient.GetAsync($"api/user/{userId}/gdpr-export");
            if (!response.IsSuccessStatusCode) return (null, string.Empty);
            var data = await response.Content.ReadAsByteArrayAsync();
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName
                ?? $"gdpr-export-{DateTime.Now:yyyyMMdd}.json";
            return (data, fileName.Trim('"'));
        }

        // Încarcă o poză de profil nouă ca multipart/form-data (content-type fixat la image/jpeg)
        // și returnează URL-ul imaginii salvate, sau null la eroare
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

        // Obține profilul unui utilizator după id; returnează null la eroare sau dacă nu există
        public async Task<UserProfileDTO?> GetUserByIdAsync(Guid userId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"api/user/{userId}");
                if (!response.IsSuccessStatusCode)
                    return null;

                return await response.Content.ReadFromJsonAsync<UserProfileDTO>();
            }
            catch
            {
                return null;
            }
        }

        // Obține profilul unui utilizator după adresa de email; returnează null la eroare sau dacă nu există
        public async Task<UserProfileDTO?> GetUserByEmailAsync(string email)
        {
            try
            {
                var response = await _httpClient.GetAsync($"api/user/email/{email}");
                if (!response.IsSuccessStatusCode)
                    return null;

                return await response.Content.ReadFromJsonAsync<UserProfileDTO>();
            }
            catch
            {
                return null;
            }
        }

        // DTO local pentru deserializarea răspunsului de upload (conține doar URL-ul imaginii)
        public class UploadResult
        {
            public string? ImageUrl { get; set; }
        }
    }
}
