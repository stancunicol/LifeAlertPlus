using LifeAlertPlus.Shared.DTOs.Requests.Measurement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Domain.Entities;
using System.Security.Claims;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class MeasurementController : ControllerBase
    {
        private readonly IMeasurementService _measurementService;
        private readonly Services.AlertMonitorService _alertMonitor;
        private readonly IUserMonitoredService _userMonitoredService;

        public MeasurementController(IMeasurementService measurementService, Services.AlertMonitorService alertMonitor, IUserMonitoredService userMonitoredService)
        {
            _measurementService = measurementService;
            _alertMonitor = alertMonitor;
            _userMonitoredService = userMonitoredService;
        }

        private async Task<bool> UserOwnsMonitoredAsync(Guid monitoredId)
        {
            var callerIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(callerIdStr, out var callerId)) return false;
            var owned = await _userMonitoredService.GetMonitoredPeopleByUserIdAsync(callerId);
            return owned.Any(m => m.Id == monitoredId);
        }

        [HttpPost]
        public async Task<IActionResult> AddMeasurement([FromBody] MeasurementRequestDTO measurementDto)
        {
            if(measurementDto == null)
                return BadRequest(new { Message = "Invalid measurement data." });

            if(string.IsNullOrWhiteSpace(measurementDto.Name) || string.IsNullOrWhiteSpace(measurementDto.Activity) || string.IsNullOrWhiteSpace(measurementDto.Coordinates))
                return BadRequest(new { Message = "Name, Activity and Coordinates are required." });

            if(measurementDto.Pulse <= 0 || measurementDto.Temperature <= 0)
                return BadRequest(new { Message = "Pulse and Temperature must be greater than zero." });

            if(measurementDto.IdMonitored == Guid.Empty)
                return BadRequest(new { Message = "IdMonitored is required." });

            if (!await UserOwnsMonitoredAsync(measurementDto.IdMonitored))
                return Forbid();

            var measurement = new Measurement
            {
                Id = Guid.NewGuid(),
                Name = measurementDto.Name,
                Activity = measurementDto.Activity,
                IsFall = measurementDto.IsFall,
                IdMonitored = measurementDto.IdMonitored,
                Pulse = measurementDto.Pulse,
                Temperature = measurementDto.Temperature,
                SpO2 = measurementDto.SpO2,
                Coordinates = measurementDto.Coordinates,
                CreatedAt = DateTime.UtcNow
            };

            await _measurementService.AddMeasurementAsync(measurement);

            // Feed the measurement to the alert monitor for sustained-alert detection
            _ = _alertMonitor.ProcessMeasurementAsync(
                measurementDto.IdMonitored,
                measurementDto.Pulse,
                measurementDto.Temperature,
                measurementDto.SpO2,
                measurementDto.IsFall,
                measurementDto.Activity,
                measurementDto.Coordinates);

            return Ok(new { Message = "Measurement added successfully." });
        }

        [HttpGet("monitored/{idMonitored}")]
        public async Task<IActionResult> GetMeasurementsByMonitoredId(Guid idMonitored, int pageNumber = 1, int pageSize = 10)
        {
            if(idMonitored == Guid.Empty)
                return BadRequest(new { Message = "Invalid monitored ID." });

            if (!await UserOwnsMonitoredAsync(idMonitored))
                return Forbid();

            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Clamp(pageSize, 1, 100);
            var measurements = await _measurementService.GetMeasurementsByMonitoredIdAsync(idMonitored, pageNumber, pageSize);
            return Ok(measurements);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetMeasurementById(Guid id)
        {
            if(id == Guid.Empty)
                return BadRequest(new { Message = "Invalid measurement ID." });

            var measurement = await _measurementService.GetMeasurementByIdAsync(id);
            if (measurement == null)
                return NotFound(new { Message = "Measurement not found." });

            if (!await UserOwnsMonitoredAsync(measurement.IdMonitored))
                return Forbid();

            return Ok(measurement);
        }

        [Authorize(Roles = "Admin")]
        [HttpGet("today/count")]
        public async Task<IActionResult> GetTodayMeasurementsCount()
        {
            var count = await _measurementService.GetTodayMeasurementsCountAsync();
            return Ok(new { Count = count });
        }
    }
}