using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using LifeAlertPlus.Shared.DTOs.Responses.ESP;
using Microsoft.AspNetCore.Authorization;
using LifeAlertPlus.Application.IServices;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class ESPController(
        IConfiguration configuration,
        ILogger<ESPController> logger,
        Services.SimulationManager simulationManager,
        IMonitoredService monitoredService,
        IUserMonitoredService userMonitoredService) : ControllerBase
    {
        [HttpGet("data/{serial}")]
        public async Task<IActionResult> GetESPData(string serial)
        {
            var callerIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(callerIdStr, out var callerId))
                return Unauthorized();

            var monitored = await monitoredService.GetMonitoredPersonByDeviceSerialNumberAsync(serial);
            if (monitored == null)
                return NotFound(new { Message = "Device not found." });

            var owned = await userMonitoredService.GetMonitoredPeopleByUserIdAsync(callerId);
            if (!owned.Any(m => m.Id == monitored.Id))
                return Forbid();

            var data = simulationManager.GetData(serial);
            if (data != null)
            {
                data.IsAvailable = true;
                data.ErrorMessage = null;
                return Ok(data);
            }

            return Ok(CreateUnavailableResponse(serial, "No data yet — waiting for device."));
        }

        [HttpPost("ingest")]
        [AllowAnonymous]
        public IActionResult IngestESPData([FromBody] ESPDataResponseDTO payload)
        {
            var expectedKey = configuration["Urls:EspDeviceKey"];
            var providedKey = Request.Headers["X-Device-Key"].ToString();

            if (string.IsNullOrWhiteSpace(expectedKey) || providedKey != expectedKey)
                return Unauthorized(new { Message = "Invalid device key." });

            if (string.IsNullOrWhiteSpace(payload?.Serial))
                return BadRequest(new { Message = "Serial is required." });

            payload.Serial = payload.Serial.Trim();
            if (payload.Date == 0) payload.Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            payload.IsAvailable = true;
            payload.ErrorMessage = null;

            simulationManager.SetData(payload);
            logger.LogDebug("ESP data ingested from {Serial}", payload.Serial);

            return Ok();
        }

        [HttpPost("simulate")]
        [Authorize(Roles = "Admin")]
        public IActionResult Simulate([FromBody] ESPDataResponseDTO payload)
        {
            if (string.IsNullOrWhiteSpace(payload.Serial))
                return BadRequest("Serial is required.");

            payload.Serial = payload.Serial.Trim();
            if (payload.Date == 0) payload.Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            payload.IsAvailable = true;
            payload.ErrorMessage = null;

            simulationManager.SetData(payload);
            logger.LogInformation("Simulated ESP data stored for serial {Serial}", payload.Serial);

            return Ok(payload);
        }

        private static ESPDataResponseDTO CreateUnavailableResponse(string serial, string message) =>
            new()
            {
                Serial       = serial,
                Date         = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                IsAvailable  = false,
                ErrorMessage = message,
                Mpu6050      = [],
                Gyro         = [],
            };
    }
}
