using System.Net.Http.Json;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Shared.DTOs.Requests.Notification;
using LifeAlertPlus.Shared.DTOs.Responses.Notification;

namespace LifeAlertPlus.Client.Services
{
    public class NotificationService
    {
        private readonly HttpClient _httpClient;

        public NotificationService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<NotificationPagedResponseDTO?> GetPagedAsync(
            int page = 1, int pageSize = 10, string? type = null, bool unreadOnly = false, Guid? monitoredId = null)
        {
            var url = $"api/notification?page={page}&pageSize={pageSize}&unreadOnly={unreadOnly}";
            if (!string.IsNullOrWhiteSpace(type))
                url += $"&type={Uri.EscapeDataString(type)}";
            if (monitoredId.HasValue)
                url += $"&monitoredId={monitoredId.Value}";

            return await _httpClient.GetFromJsonAsync<NotificationPagedResponseDTO>(url);
        }

        public async Task<bool> MarkAsReadAsync(Guid id)
        {
            var response = await _httpClient.PatchAsync($"api/notification/{id}/read", null);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> MarkAllAsReadAsync()
        {
            var response = await _httpClient.PatchAsync("api/notification/read-all", null);
            return response.IsSuccessStatusCode;
        }

        public async Task<List<Notification>> GetRecentNotificationsAsync(int count = 20)
        {
            var result = await _httpClient.GetFromJsonAsync<List<Notification>>("api/notification/recent?count=" + count);
            return result ?? new List<Notification>();
        }

        public async Task<List<PendingFeedbackDTO>> GetPendingFeedbackAsync()
        {
            try
            {
                var result = await _httpClient.GetFromJsonAsync<List<PendingFeedbackDTO>>("api/notification/pending-feedback");
                return result ?? new List<PendingFeedbackDTO>();
            }
            catch
            {
                return new List<PendingFeedbackDTO>();
            }
        }

        public async Task<bool> SubmitFeedbackAsync(Guid id, bool wasReal)
        {
            var response = await _httpClient.PatchAsJsonAsync(
                $"api/notification/{id}/feedback",
                new NotificationFeedbackRequestDTO { WasReal = wasReal });
            return response.IsSuccessStatusCode;
        }
    }
}
