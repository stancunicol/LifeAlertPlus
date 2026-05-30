using System.Net.Http.Json;

namespace LifeAlertPlus.Client.Services
{
    public class AdminApiClient(HttpClient http)
    {
        public async Task<List<AuditEntryDTO>> GetAuditLogAsync(int limit = 100)
        {
            try
            {
                var result = await http.GetFromJsonAsync<List<AuditEntryDTO>>($"api/admin/audit-log?limit={limit}");
                return result ?? new();
            }
            catch { return new(); }
        }

        public async Task<List<ErrorLogEntryDTO>> GetErrorLogAsync(int limit = 100)
        {
            try
            {
                var result = await http.GetFromJsonAsync<List<ErrorLogEntryDTO>>($"api/admin/error-log?limit={limit}");
                return result ?? new();
            }
            catch { return new(); }
        }

        public sealed record AuditEntryDTO(
            Guid Id, DateTime Timestamp, string User,
            string Action, string Details, string Category);

        public sealed record ErrorLogEntryDTO(
            DateTime Timestamp, string Level,
            string Source, string Message, string Details);
    }
}
