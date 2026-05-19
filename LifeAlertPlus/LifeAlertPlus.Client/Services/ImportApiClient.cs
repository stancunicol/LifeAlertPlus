using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace LifeAlertPlus.Client.Services
{
    public class ImportApiClient
    {
        private readonly HttpClient _httpClient;
        public ImportApiClient(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<ImportResult> ImportESPDataAsync(string jsonContent)
        {
            var response = await _httpClient.PostAsJsonAsync("api/import/esp-data", jsonContent);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ImportResult>();
                if (result != null)
                {
                    result.Success = true;
                    return result;
                }
                return new ImportResult { Success = false, Message = "Eroare la parsarea răspunsului." };
            }
            else
            {
                var error = await response.Content.ReadFromJsonAsync<ImportResult>();
                return error ?? new ImportResult { Success = false, Message = "Import eșuat." };
            }
        }

        public async Task<ImportResult> ConfirmESPDataAsync(object[] data)
        {
            var response = await _httpClient.PostAsJsonAsync("api/import/esp-data/confirm", data);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ImportResult>();
                if (result != null)
                {
                    result.Success = true;
                    return result;
                }
                return new ImportResult { Success = false, Message = "Eroare la parsarea răspunsului confirmare." };
            }
            else
            {
                var error = await response.Content.ReadFromJsonAsync<ImportResult>();
                return error ?? new ImportResult { Success = false, Message = "Confirmare eșuată." };
            }
        }
    }

    public class ImportResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string[]? Errors { get; set; }
        public object[]? Data { get; set; }
    }
}
