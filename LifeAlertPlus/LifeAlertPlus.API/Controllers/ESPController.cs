using Microsoft.AspNetCore.Mvc;
using LifeAlertPlus.Shared.DTOs.Responses.ESP;
using LifeAlertPlus.Shared.DTOs.Requests.ESP;
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
        Services.AlertMonitorService alertMonitorService,
        IMonitoredService monitoredService,
        IUserMonitoredService userMonitoredService,
        IMeasurementService measurementService,
        IWifiNetworkService wifiNetworkService) : BaseApiController
    {
        [HttpGet("data/{serial}")]
        public async Task<IActionResult> GetESPData(string serial)
        {
            var callerId = GetCallerId();
            if (callerId == null)
                return Unauthorized();

            var monitored = await monitoredService.GetMonitoredPersonByDeviceSerialNumberAsync(serial);
            if (monitored == null)
                return NotFound(new { Message = "Device not found." });

            var owned = await userMonitoredService.GetMonitoredPeopleByUserIdAsync(callerId.Value);
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
        public async Task<IActionResult> IngestESPData([FromBody] ESPDataResponseDTO payload)
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

            var monitored = await monitoredService.GetMonitoredPersonByDeviceSerialNumberAsync(payload.Serial);
            if (monitored == null)
            {
                logger.LogDebug("ESP data ingested from {Serial} (no monitored linked — measurement not persisted)", payload.Serial);
                return Ok();
            }

            int pulse = payload.Bpm ?? 0;
            double temperature = payload.Temperature ?? 0;
            int spo2 = 0;
            string coordinates = payload.Neo6m ?? string.Empty;
            bool isFall = payload.IsFall;
            // Firmware classifies movement over the last ~5s window from the same MPU
            // stream the fall detector uses (50 Hz). Persist the label so the behavioral
            // profile can compute movement rate / sleep probability per hour over 14 days.
            string activity = string.IsNullOrWhiteSpace(payload.Activity)
                ? string.Empty
                : payload.Activity.Trim();

            var measurement = new Domain.Entities.Measurement
            {
                Id = Guid.NewGuid(),
                Name = "ESP Device",
                Activity = activity,
                IsFall = isFall,
                IdMonitored = monitored.Id,
                Pulse = pulse,
                SpO2 = spo2,
                Temperature = temperature,
                Coordinates = coordinates,
                CreatedAt = DateTime.UtcNow
            };
            await measurementService.AddMeasurementAsync(measurement);

            _ = alertMonitorService.ProcessMeasurementAsync(
                monitored.Id, pulse, temperature, spo2, isFall: isFall,
                activity: activity,
                coordinates: coordinates);

            if (isFall)
                logger.LogWarning("ESP {Serial} reported FALL — triggering critical alert flow", payload.Serial);
            else
                logger.LogDebug("ESP data ingested from {Serial}: pulse={Pulse} temp={Temp}", payload.Serial, pulse, temperature);
            return Ok();
        }

        [HttpPost("panic")]
        [AllowAnonymous]
        public async Task<IActionResult> PanicAlert([FromBody] ESPPanicDTO payload)
        {
            var expectedKey = configuration["Urls:EspDeviceKey"];
            var providedKey = Request.Headers["X-Device-Key"].ToString();
            if (string.IsNullOrWhiteSpace(expectedKey) || providedKey != expectedKey)
                return Unauthorized(new { Message = "Invalid device key." });

            if (string.IsNullOrWhiteSpace(payload?.Serial))
                return BadRequest(new { Message = "Serial is required." });

            var monitored = await monitoredService.GetMonitoredPersonByDeviceSerialNumberAsync(payload.Serial.Trim());
            if (monitored == null)
                return NotFound(new { Message = "Device not found." });

            await alertMonitorService.TriggerPanicAlertAsync(monitored.Id, payload.Coordinates);
            logger.LogWarning("Panic alert triggered by device {Serial}", payload.Serial);
            return Ok();
        }

        [HttpGet("wifi-config/{serial}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetWifiConfig(string serial)
        {
            var expectedKey = configuration["Urls:EspDeviceKey"];
            var providedKey = Request.Headers["X-Device-Key"].ToString();
            if (string.IsNullOrWhiteSpace(expectedKey) || providedKey != expectedKey)
                return Unauthorized(new { Message = "Invalid device key." });

            if (string.IsNullOrWhiteSpace(serial))
                return BadRequest(new { Message = "Serial is required." });

            var networks = await wifiNetworkService.GetByDeviceSerialAsync(serial.Trim());
            var payload = networks
                .Select(n => new { ssid = n.Ssid, password = n.Password })
                .ToList();
            return Ok(new { networks = payload });
        }

        [HttpPost("heartbeat")]
        [AllowAnonymous]
        public IActionResult Heartbeat([FromBody] ESPHeartbeatDTO payload)
        {
            var expectedKey = configuration["Urls:EspDeviceKey"];
            var providedKey = Request.Headers["X-Device-Key"].ToString();
            if (string.IsNullOrWhiteSpace(expectedKey) || providedKey != expectedKey)
                return Unauthorized(new { Message = "Invalid device key." });

            if (string.IsNullOrWhiteSpace(payload?.Serial))
                return BadRequest(new { Message = "Serial is required." });

            simulationManager.SetHeartbeat(payload.Serial.Trim(), payload);
            logger.LogDebug("Heartbeat from {Serial}: RSSI={Rssi} heap={Heap} uptime={Uptime}s queue={Queue}",
                payload.Serial, payload.RssiDbm, payload.FreeHeapBytes, payload.UptimeSeconds, payload.QueuedMeasurements);
            return Ok();
        }

        [HttpPost("simulate")]
        [Authorize(Roles = "Admin")]
        public IActionResult Simulate([FromBody] ESPDataResponseDTO payload)
        {
            if (string.IsNullOrWhiteSpace(payload.Serial))
                return BadRequest(new { Message = "Serial is required." });

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
