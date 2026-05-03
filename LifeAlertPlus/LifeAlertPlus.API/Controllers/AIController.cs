using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LifeAlertPlus.Shared.DTOs.Requests.AI;
using LifeAlertPlus.Shared.DTOs.Responses.AI;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Infrastructure.Context;
using System.Text.Json;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class AIController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AIController> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        public AIController(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<AIController> logger,
            IServiceScopeFactory scopeFactory)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        [HttpPost("predict")]
        public async Task<IActionResult> Predict([FromBody] AIPredictionRequestDTO request)
        {
            if (request == null)
                return BadRequest(new { Message = "Invalid prediction request." });

            var aiBaseUrl = _configuration["Urls:AiServiceUrl"] ?? "http://localhost:8000";

            // Load patient-specific conditions and thresholds when MonitoredId is provided
            List<string> conditions = new();
            int? maxHr = null, minHr = null;
            double? maxTemp = null, minTemp = null;

            if (request.MonitoredId.HasValue && request.MonitoredId != Guid.Empty)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<LifeAlertPlusDbContext>();
                    var condRepo = scope.ServiceProvider.GetRequiredService<IMonitoredConditionRepository>();

                    var monitored = await db.Monitoreds.FindAsync(request.MonitoredId.Value);
                    if (monitored != null)
                    {
                        maxHr   = monitored.MaxHeartRate;
                        minHr   = monitored.MinHeartRate;
                        maxTemp = monitored.MaxTemperature;
                        minTemp = monitored.MinTemperature;
                    }

                    var conds = await condRepo.GetByMonitoredIdAsync(request.MonitoredId.Value);
                    conditions = conds.Select(c => c.ConditionKey).ToList();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load conditions/thresholds for MonitoredId {MonitoredId}. Using defaults.", request.MonitoredId);
                }
            }

            try
            {
                var client = _httpClientFactory.CreateClient();
                var payload = new
                {
                    pulse            = request.Pulse,
                    temperature      = request.Temperature,
                    spo2             = request.Spo2,
                    accel_x          = request.AccelX,
                    accel_y          = request.AccelY,
                    accel_z          = request.AccelZ,
                    gyro_x           = request.GyroX,
                    gyro_y           = request.GyroY,
                    gyro_z           = request.GyroZ,
                    conditions,
                    max_heart_rate   = maxHr,
                    min_heart_rate   = minHr,
                    max_temperature  = maxTemp,
                    min_temperature  = minTemp,
                };

                var response = await client.PostAsJsonAsync($"{aiBaseUrl}/predict", payload);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("AI service returned {StatusCode}", (int)response.StatusCode);
                    return StatusCode(502, new { Message = "AI service unavailable." });
                }

                var json = await response.Content.ReadAsStringAsync();
                var prediction = JsonSerializer.Deserialize<AIPredictionResponseDTO>(json, _jsonOptions);

                if (prediction == null)
                    return StatusCode(502, new { Message = "Invalid AI service response." });

                return Ok(prediction);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "AI service connection failed");
                return StatusCode(502, new { Message = "AI service unreachable." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling AI service");
                return StatusCode(500, new { Message = "Error processing AI prediction." });
            }
        }

        [HttpGet("health")]
        [AllowAnonymous]
        public async Task<IActionResult> Health()
        {
            var aiBaseUrl = _configuration["Urls:AiServiceUrl"] ?? "http://localhost:8000";

            try
            {
                var client = _httpClientFactory.CreateClient();
                var response = await client.GetAsync($"{aiBaseUrl}/health");
                var json = await response.Content.ReadAsStringAsync();
                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI health check failed");
                return Ok(new { status = "unavailable", error = ex.Message });
            }
        }
    }
}
