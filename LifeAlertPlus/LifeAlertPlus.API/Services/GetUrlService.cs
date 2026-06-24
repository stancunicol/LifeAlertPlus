namespace LifeAlertPlus.API.Services
{
    // Serviciu care construiește URL-urile de bază pentru API și client (Blazor WASM)
    // Necesar deoarece URL-ul se poate schimba între medii (dev, staging, producție Azure)
    // și unele scenarii (email-uri de confirmare) necesită URL-uri absolute
    public class GetUrlService
    {
        private readonly IConfiguration _configuration; // Fișierul appsettings.json / variabile de mediu
        private readonly IHttpContextAccessor _httpContextAccessor; // Accesul la cererea HTTP curentă

        public GetUrlService(IConfiguration configuration, IHttpContextAccessor httpContextAccessor)
        {
            _configuration = configuration;
            _httpContextAccessor = httpContextAccessor;
        }

        // Returnează URL-ul de bază al API-ului (ex: "https://api.lifealertplus.com")
        // Folosit pentru link-urile de confirmare email care trebuie să ajungă înapoi la API
        public string GetApiBaseUrl()
        {
            // Preferăm URL-ul configurat explicit (mai sigur în producție)
            var configured = _configuration["Urls:ApiBaseUrl"];
            if (!string.IsNullOrWhiteSpace(configured))
                return configured.TrimEnd('/'); // Eliminăm slash-ul final pentru URL-uri consistente

            // Fallback: construim URL-ul din cererea HTTP curentă (bun pentru dev local)
            var request = _httpContextAccessor.HttpContext?.Request;
            return request != null ? $"{request.Scheme}://{request.Host}" : string.Empty;
        }

        // Returnează URL-ul de bază al clientului Blazor WASM (ex: "https://app.lifealertplus.com")
        // Folosit pentru link-uri de redirect după confirmare email (care duc la interfața utilizatorului)
        public string GetClientBaseUrl()
        {
            var configured = _configuration["Urls:ClientBaseUrl"];
            if (!string.IsNullOrWhiteSpace(configured))
                return configured.TrimEnd('/');

            // Fallback la URL-ul din cerere (în dev, API și clientul pot fi pe același host)
            var request = _httpContextAccessor.HttpContext?.Request;
            return request != null ? $"{request.Scheme}://{request.Host}" : string.Empty;
        }
    }
}
