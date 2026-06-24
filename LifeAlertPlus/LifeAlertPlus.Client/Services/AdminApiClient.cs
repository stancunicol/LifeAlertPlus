using System.Net.Http.Json;

namespace LifeAlertPlus.Client.Services
{
    // Client HTTP pentru endpoint-urile /api/admin — citește jurnalul de audit și jurnalul de erori
    // al aplicației, folosit în panoul de administrare
    public class AdminApiClient(HttpClient http)
    {
        // Obține ultimele intrări din jurnalul de audit (acțiuni efectuate de utilizatori);
        // returnează listă vidă la eroare de rețea/deserializare
        public async Task<List<AuditEntryDTO>> GetAuditLogAsync(int limit = 100)
        {
            try
            {
                var result = await http.GetFromJsonAsync<List<AuditEntryDTO>>($"api/admin/audit-log?limit={limit}");
                return result ?? new();
            }
            catch { return new(); }
        }

        // Obține ultimele intrări din jurnalul de erori al aplicației; returnează listă vidă la eroare
        public async Task<List<ErrorLogEntryDTO>> GetErrorLogAsync(int limit = 100)
        {
            try
            {
                var result = await http.GetFromJsonAsync<List<ErrorLogEntryDTO>>($"api/admin/error-log?limit={limit}");
                return result ?? new();
            }
            catch { return new(); }
        }

        // DTO local pentru o intrare din jurnalul de audit
        public sealed record AuditEntryDTO(
            Guid Id, DateTime Timestamp, string User,
            string Action, string Details, string Category);

        // DTO local pentru o intrare din jurnalul de erori
        public sealed record ErrorLogEntryDTO(
            DateTime Timestamp, string Level,
            string Source, string Message, string Details);
    }
}
