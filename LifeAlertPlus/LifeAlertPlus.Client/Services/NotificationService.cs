using System.Net.Http.Json;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Shared.DTOs.Requests.Notification;
using LifeAlertPlus.Shared.DTOs.Responses.Notification;

namespace LifeAlertPlus.Client.Services
{
    // Client HTTP pentru endpoint-urile /api/notification — listare paginată, marcare citit,
    // feedback pe notificări (confirmare alarmă reală/falsă) și numărul de notificări necitite
    public class NotificationService
    {
        private readonly HttpClient _httpClient;

        public NotificationService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        // Obține notificările paginat, cu filtre opționale după tip, status necitit și persoană monitorizată
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

        // Notifică UI-ul (ex: badge cu numărul de necitite) că numărul de notificări necitite s-a schimbat
        public event Action? OnUnreadCountChanged;

        // Marchează o notificare ca citită și declanșează actualizarea contorului din UI
        public async Task<bool> MarkAsReadAsync(Guid id)
        {
            var response = await _httpClient.PatchAsync($"api/notification/{id}/read", null);
            if (response.IsSuccessStatusCode) OnUnreadCountChanged?.Invoke();
            return response.IsSuccessStatusCode;
        }

        // Marchează toate notificările utilizatorului curent ca citite
        public async Task<bool> MarkAllAsReadAsync()
        {
            var response = await _httpClient.PatchAsync("api/notification/read-all", null);
            if (response.IsSuccessStatusCode) OnUnreadCountChanged?.Invoke();
            return response.IsSuccessStatusCode;
        }

        // Obține cele mai recente notificări (ex: pentru dropdown din header)
        public async Task<List<Notification>> GetRecentNotificationsAsync(int count = 20)
        {
            var result = await _httpClient.GetFromJsonAsync<List<Notification>>("api/notification/recent?count=" + count);
            return result ?? new List<Notification>();
        }

        // Obține notificările care încă așteaptă feedback de la utilizator (confirmare alarmă reală/falsă);
        // returnează listă vidă la eroare
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

        // Trimite feedback-ul utilizatorului pentru o notificare (a fost o alarmă reală sau falsă)
        public async Task<bool> SubmitFeedbackAsync(Guid id, bool wasReal)
        {
            var response = await _httpClient.PatchAsJsonAsync(
                $"api/notification/{id}/feedback",
                new NotificationFeedbackRequestDTO { WasReal = wasReal });
            return response.IsSuccessStatusCode;
        }
    }
}
