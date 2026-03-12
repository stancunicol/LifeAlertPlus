namespace LifeAlertPlus.API.Services
{
    public class GetUrlService
    {
        private readonly IConfiguration _configuration;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public GetUrlService(IConfiguration configuration, IHttpContextAccessor httpContextAccessor)
        {
            _configuration = configuration;
            _httpContextAccessor = httpContextAccessor;
        }

        public string GetApiBaseUrl()
        {
            var configured = _configuration["Urls:ApiBaseUrl"];
            if (!string.IsNullOrWhiteSpace(configured))
                return configured.TrimEnd('/');

            var request = _httpContextAccessor.HttpContext?.Request;
            return request != null ? $"{request.Scheme}://{request.Host}" : string.Empty;
        }

        public string GetClientBaseUrl()
        {
            var configured = _configuration["Urls:ClientBaseUrl"];
            if (!string.IsNullOrWhiteSpace(configured))
                return configured.TrimEnd('/');

            var request = _httpContextAccessor.HttpContext?.Request;
            return request != null ? $"{request.Scheme}://{request.Host}" : string.Empty;
        }
    }
}