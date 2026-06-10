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
        IWifiNetworkService wifiNetworkService,
        Services.DeviceTestLogService deviceTestLogService) : BaseApiController
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

            if (!IsAdminRole())
            {
                var owned = await userMonitoredService.GetMonitoredPeopleByUserIdAsync(callerId.Value);
                if (!owned.Any(m => m.Id == monitored.Id))
                    return Forbid();
            }

            var data = simulationManager.GetData(serial);
            if (data != null)
            {
                data.IsAvailable = true;
                data.ErrorMessage = null;
            }
            else
            {
                data = CreateUnavailableResponse(serial, "No data yet — waiting for device.");
            }

            // Attach latest heartbeat diagnostics (battery, RSSI, uptime) if available.
            var hb = simulationManager.GetHeartbeat(serial.Trim());
            if (hb.HasValue)
            {
                data.RssiDbm        = hb.Value.Data.RssiDbm;
                data.FreeHeapBytes  = hb.Value.Data.FreeHeapBytes;
                data.UptimeSeconds  = (int)Math.Min(hb.Value.Data.UptimeSeconds, int.MaxValue);
                data.HeartbeatAge   = (int)(DateTime.UtcNow - hb.Value.ReceivedAt).TotalSeconds;
            }

            return Ok(data);
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

            // Rate limit: max 4 ingest calls per 60 s per serial to prevent data flooding.
            if (!alertMonitorService.IsIngestAllowed(payload.Serial))
            {
                logger.LogWarning("Ingest rate limit exceeded for serial {Serial}", payload.Serial);
                return StatusCode(429, new { Message = "Rate limit exceeded. Please slow down." });
            }

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

            if (monitored.IsArchived)
            {
                logger.LogInformation("ESP data from {Serial} ignored — monitored {MonitoredId} is archived", payload.Serial, monitored.Id);
                return Ok(new { Message = "Monitored person is archived. Data not persisted." });
            }

            // Normalize: firmware may send only Max30100 without Bpm/Spo2 fields
            int pulse = payload.Bpm
                ?? (payload.Max30100?.Count >= 1 ? payload.Max30100[0] : 0);
            int spo2 = payload.Spo2
                ?? (payload.Max30100?.Count >= 2 ? payload.Max30100[1] : 0);
            double temperature = payload.Temperature ?? 0;

            // Backfill Bpm/Spo2 so the in-memory cache stays consistent
            payload.Bpm  ??= pulse;
            payload.Spo2 ??= spo2;
            string coordinates = payload.Neo6m ?? string.Empty;
            bool isFall = payload.IsFall;
            // Firmware classifies movement over the last ~5s window from the same MPU
            // stream the fall detector uses (50 Hz). Persist the label so the behavioral
            // profile can compute movement rate / sleep probability per hour over 7 days.
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

            deviceTestLogService.Log(new Services.DeviceTestLogEntry
            {
                Type        = isFall ? "fall" : "measurement",
                Timestamp   = DateTime.UtcNow.ToString("O"),
                Serial      = payload.Serial,
                Pulse       = pulse,
                SpO2        = spo2,
                Temperature = temperature,
                Coordinates = string.IsNullOrWhiteSpace(coordinates) ? null : coordinates,
                Activity    = string.IsNullOrWhiteSpace(activity) ? null : activity,
                IsFall      = isFall ? true : null,
                Battery     = payload.Battery
            });

            _ = Task.Run(async () =>
            {
                try
                {
                    await alertMonitorService.ProcessMeasurementAsync(
                        monitored.Id, pulse, temperature, spo2, isFall: isFall,
                        activity: activity,
                        coordinates: coordinates);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "ProcessMeasurementAsync failed for serial {Serial} (monitored {MonitoredId})",
                        payload.Serial, monitored.Id);
                }
            });

            if (isFall)
                logger.LogWarning("ESP {Serial} reported FALL — triggering critical alert flow", payload.Serial);
            else
                logger.LogDebug("ESP data ingested from {Serial}: pulse={Pulse} temp={Temp}", payload.Serial, pulse, temperature);

            // Battery low check — fires a push notification when below threshold.
            if (payload.Battery.HasValue)
                _ = alertMonitorService.CheckBatteryAsync(monitored.Id, payload.Serial, payload.Battery.Value);

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

            if (monitored.IsArchived)
            {
                logger.LogInformation("Panic alert from {Serial} ignored — monitored {MonitoredId} is archived", payload.Serial, monitored.Id);
                return Ok(new { Message = "Monitored person is archived. Panic alert not triggered." });
            }

            await alertMonitorService.TriggerPanicAlertAsync(monitored.Id, payload.Coordinates);
            logger.LogWarning("Panic alert triggered by device {Serial}", payload.Serial);

            deviceTestLogService.Log(new Services.DeviceTestLogEntry
            {
                Type        = "panic",
                Timestamp   = DateTime.UtcNow.ToString("O"),
                Serial      = payload.Serial,
                Coordinates = string.IsNullOrWhiteSpace(payload.Coordinates) ? null : payload.Coordinates
            });

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

        [HttpDelete("simulate/{serial}")]
        [Authorize(Roles = "Admin")]
        public IActionResult ClearSimulatedData(string serial)
        {
            if (string.IsNullOrWhiteSpace(serial))
                return BadRequest(new { Message = "Serial is required." });

            simulationManager.ClearData(serial.Trim());
            logger.LogInformation("Simulated data cleared for serial {Serial}", serial);
            return Ok(new { Message = "Simulated data cleared." });
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
