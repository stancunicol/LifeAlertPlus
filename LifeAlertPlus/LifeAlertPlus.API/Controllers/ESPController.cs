using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using LifeAlertPlus.Shared.DTOs.Responses.ESP;
using Microsoft.AspNetCore.Authorization;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class ESPController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ESPController> _logger;

        private static readonly JsonSerializerOptions _jsonOptions =
            new() { PropertyNameCaseInsensitive = true };

        public ESPController(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<ESPController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpGet("data/{serial}")]
        public async Task<IActionResult> GetESPData(string serial, CancellationToken cancellationToken)
        {
            var espBaseUrl = _configuration["Urls:EspDeviceUrl"] ?? "http://localhost:5000";
            var url = $"{espBaseUrl}/api/data/{serial}";
            var client = _httpClientFactory.CreateClient();

            try
            {
                var response = await client.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                    return StatusCode((int)response.StatusCode, $"Failed to fetch data for serial {serial}");

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var data = JsonSerializer.Deserialize<ESPDataResponseDTO>(json, _jsonOptions);

                if (data == null)
                    return BadRequest("Invalid data format received.");

                return Ok(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching ESP data for serial {Serial}", serial);
                return StatusCode(500, "Error fetching ESP data.");
            }
        }
    }
}