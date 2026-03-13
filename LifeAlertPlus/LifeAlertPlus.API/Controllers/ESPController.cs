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

            using var espCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            espCts.CancelAfter(TimeSpan.FromSeconds(10));

            try
            {
                var response = await client.GetAsync(url, espCts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("ESP returned status code {StatusCode} for serial {Serial}", (int)response.StatusCode, serial);
                    return Ok(CreateUnavailableResponse(serial, $"ESP returned HTTP {(int)response.StatusCode}."));
                }

                var json = await response.Content.ReadAsStringAsync(espCts.Token);
                var data = JsonSerializer.Deserialize<ESPDataResponseDTO>(json, _jsonOptions);

                if (data == null)
                {
                    _logger.LogWarning("ESP returned invalid JSON payload for serial {Serial}", serial);
                    return Ok(CreateUnavailableResponse(serial, "Invalid ESP payload."));
                }

                data.Serial = string.IsNullOrWhiteSpace(data.Serial) ? serial : data.Serial;
                data.IsAvailable = true;
                data.ErrorMessage = null;

                return Ok(data);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("ESP device timeout for serial {Serial}", serial);
                return Ok(CreateUnavailableResponse(serial, "ESP device timeout."));
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "ESP connection failed for serial {Serial}", serial);
                return Ok(CreateUnavailableResponse(serial, "ESP device unreachable."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching ESP data for serial {Serial}", serial);
                return Ok(CreateUnavailableResponse(serial, "ESP data unavailable."));
            }
        }

        private static ESPDataResponseDTO CreateUnavailableResponse(string serial, string message)
        {
            return new ESPDataResponseDTO
            {
                Serial = serial,
                Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                IsAvailable = false,
                ErrorMessage = message,
                Mpu6050 = new List<int>(),
                Gyro = new List<int>(),
                Max30100 = null,
                Neo6m = null,
                Hmc5883l = 0
            };
        }
    }
}