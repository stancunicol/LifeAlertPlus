using System.Net.Http.Json;
using LifeAlertPlus.Shared.DTOs.Requests.Monitored;
using LifeAlertPlus.Shared.DTOs.Responses.ESP;
using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Client.Services
{
    // Client HTTP pentru endpoint-urile /api/monitored și /api/esp/data — CRUD persoane monitorizate
    // (adăugare, actualizare, arhivare/restaurare, ștergere/eliminare, reactivare) și citirea
    // ultimelor date raportate de dispozitivul ESP asociat
    public class MonitoredApiClient
    {
        private readonly HttpClient _httpClient;

        public MonitoredApiClient(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        // Adaugă o persoană monitorizată nouă
        public async Task<bool> AddMonitoredPersonAsync(MonitorAddRequestDTO monitoredPerson)
        {
            var response = await _httpClient.PostAsJsonAsync("api/monitored/add", monitoredPerson);
            return response.IsSuccessStatusCode;
        }

        // Obține o persoană monitorizată după id; returnează null dacă nu există sau cererea a eșuat
        public async Task<Monitored?> GetMonitoredPersonByIdAsync(Guid id)
        {
            var response = await _httpClient.GetAsync($"api/monitored/id/{id}");

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<Monitored>();
            return result;
        }

        // Obține ultimele date raportate de dispozitivul ESP (după numărul de serie); dacă DTO-ul
        // returnat nu are serialul populat, îl completează cu valoarea cunoscută din parametru
        public async Task<ESPDataResponseDTO?> GetEspDataAsync(string deviceSerialNumber, CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.GetAsync($"api/esp/data/{deviceSerialNumber}", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            var dto = await response.Content.ReadFromJsonAsync<ESPDataResponseDTO>(cancellationToken: cancellationToken);
            if (dto != null && string.IsNullOrWhiteSpace(dto.Serial))
                dto.Serial = deviceSerialNumber;

            return dto;
        }

        // Actualizează datele unei persoane monitorizate existente
        public async Task<bool> UpdateMonitoredPersonAsync(Guid id, MonitorUpdateRequestDTO dto)
        {
            var response = await _httpClient.PutAsJsonAsync($"api/monitored/update/{id}", dto);
            return response.IsSuccessStatusCode;
        }

        // Arhivează o persoană monitorizată (soft — rămâne în baza de date dar e ascunsă din listele active)
        public async Task<bool> ArchiveMonitoredPersonAsync(Guid id)
        {
            var response = await _httpClient.PutAsync($"api/monitored/archive/{id}", null);
            return response.IsSuccessStatusCode;
        }

        // Restaurează o persoană monitorizată arhivată
        public async Task<bool> RestoreMonitoredPersonAsync(Guid id)
        {
            var response = await _httpClient.PutAsync($"api/monitored/restore/{id}", null);
            return response.IsSuccessStatusCode;
        }

        // Șterge definitiv o persoană monitorizată
        public async Task<bool> DeleteMonitoredPersonAsync(Guid id)
        {
            var response = await _httpClient.DeleteAsync($"api/monitored/{id}");
            return response.IsSuccessStatusCode;
        }

        // Elimină asocierea persoanei monitorizate cu utilizatorul curent; backend-ul informează
        // dacă acesta era ultimul proprietar (caz în care persoana ar putea fi ștearsă/orfanizată)
        public async Task<RemoveMonitoredResult?> RemoveMonitoredAsync(Guid id)
        {
            var response = await _httpClient.DeleteAsync($"api/monitored/{id}/remove");
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<RemoveMonitoredResult>();
        }

        // Reactivează o persoană monitorizată (ex: după eliminare/dezactivare)
        public async Task<bool> ReactivateMonitoredAsync(Guid id)
        {
            var response = await _httpClient.PutAsync($"api/monitored/reactivate/{id}", null);
            return response.IsSuccessStatusCode;
        }
    }

    // Rezultatul operației de eliminare a asocierii: indică dacă utilizatorul curent era
    // ultimul proprietar al persoanei monitorizate, plus un mesaj descriptiv pentru UI
    public record RemoveMonitoredResult(bool WasLastOwner, string Message);
}
