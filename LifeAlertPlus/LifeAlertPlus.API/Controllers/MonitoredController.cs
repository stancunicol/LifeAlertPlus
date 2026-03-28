using LifeAlertPlus.Shared.DTOs.Requests.Monitored;
using LifeAlertPlus.Application.IServices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class MonitoredController : ControllerBase
    {
        private readonly IMonitoredService _monitoredService;
        private readonly IUserMonitoredService _userMonitoredService;

        public MonitoredController(IMonitoredService monitoredService, IUserMonitoredService userMonitoredService)
        {
            _monitoredService = monitoredService;
            _userMonitoredService = userMonitoredService;
        }

        [HttpPost("add")]
        public async Task<IActionResult> AddMonitoredPerson([FromBody] MonitorAddRequestDTO newMonitored)
        {
            var callerIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (callerIdStr == null || !Guid.TryParse(callerIdStr, out var callerId))
                return Unauthorized(new { Message = "Invalid token." });

            var newPerson = newMonitored.MonitoredPerson;

            if(newPerson == null)
            {
                return BadRequest(new { Message = "Invalid monitored person data." });
            }

            if(string.IsNullOrEmpty(newPerson.FirstName) || string.IsNullOrEmpty(newPerson.LastName) || 
            string.IsNullOrEmpty(newPerson.DeviceSerialNumber) || string.IsNullOrEmpty(newPerson.Address) ||
            string.IsNullOrEmpty(newPerson.Gender))
            {
                return BadRequest(new { Message = "All fields are required." });
            }

            var existingPerson = await _monitoredService.GetMonitoredPersonByDeviceSerialNumberAsync(newPerson.DeviceSerialNumber);
            if(existingPerson != null)
            {
                return Conflict(new { Message = "A monitored person with the same device serial number already exists." });
            }

            try
            {
                var createdPerson = await _monitoredService.AddMonitoredPersonAsync(newPerson);
                if (createdPerson == null)
                {
                    return StatusCode(500, new { Message = "Failed to add monitored person." });
                }

                createdPerson.IsActive = true;
                await _userMonitoredService.AddMonitoredPersonToUserAsync(callerId, createdPerson.Id);

                return Ok(new { Message = "Monitored person added successfully.", MonitoredPerson = createdPerson });
            }
            catch (Exception ex)
            {
                // Log the error details
                Console.WriteLine($"Error adding monitored person: {ex.Message}");
                return StatusCode(500, new { Message = "An error occurred while adding monitored person.", Details = ex.Message });
            }
        }

        [Authorize(Roles = "Admin")]
        [HttpGet("serial/{deviceSerialNumber}")]
        public async Task<IActionResult> GetMonitoredPersonByDeviceSerialNumber([FromRoute] string deviceSerialNumber)
        {
            var callerIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (callerIdStr == null || !Guid.TryParse(callerIdStr, out var callerId))
                return Unauthorized(new { Message = "Invalid token." });

            var monitoredPerson = await _monitoredService.GetMonitoredPersonByDeviceSerialNumberAsync(deviceSerialNumber);
            if (monitoredPerson == null)
                return NotFound(new { Message = "Monitored person not found." });

            var owned = await _userMonitoredService.GetMonitoredPeopleByUserIdAsync(callerId);
            var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value ?? User.FindFirst("role")?.Value ?? string.Empty;
            if (!owned.Any(m => m.Id == monitoredPerson.Id))
                return Forbid();

            return Ok(monitoredPerson);
        }

        [HttpGet("id/{id:guid}")]
        public async Task<IActionResult> GetMonitoredPersonById([FromRoute] Guid id)
        {
            var callerIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (callerIdStr == null || !Guid.TryParse(callerIdStr, out var callerId))
                return Unauthorized(new { Message = "Invalid token." });

            var monitoredPerson = await _monitoredService.GetMonitoredPersonByIdAsync(id);
            if (monitoredPerson == null)
                return NotFound(new { Message = "Monitored person not found." });

            var owned = await _userMonitoredService.GetMonitoredPeopleByUserIdAsync(callerId);
            var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value ?? User.FindFirst("role")?.Value ?? string.Empty;
            if (!owned.Any(m => m.Id == id) && !IsAdminRole(roleClaim))
                return Forbid();

            return Ok(monitoredPerson);
        }

        [HttpPut("update/{id:guid}")]
        public async Task<IActionResult> UpdateMonitoredPerson([FromRoute] Guid id, [FromBody] MonitorUpdateRequestDTO dto)
        {
            var callerIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (callerIdStr == null || !Guid.TryParse(callerIdStr, out var callerId))
                return Unauthorized(new { Message = "Invalid token." });

            var existing = await _monitoredService.GetMonitoredPersonByIdAsync(id);
            if (existing == null)
                return NotFound(new { Message = "Monitored person not found." });

            var owned = await _userMonitoredService.GetMonitoredPeopleByUserIdAsync(callerId);
            var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value ?? User.FindFirst("role")?.Value ?? string.Empty;
            if (!owned.Any(m => m.Id == id) && !IsAdminRole(roleClaim))
                return Forbid();

            if (dto.DeviceSerialNumber != existing.DeviceSerialNumber)
            {
                var conflict = await _monitoredService.GetMonitoredPersonByDeviceSerialNumberAsync(dto.DeviceSerialNumber);
                if (conflict != null && conflict.Id != id)
                    return Conflict(new { Message = "A monitored person with this device serial number already exists." });
            }

            existing.FirstName = dto.FirstName;
            existing.LastName = dto.LastName;
            existing.Birthdate = dto.Birthdate;
            existing.Gender = dto.Gender;
            existing.Address = dto.Address;
            existing.DeviceSerialNumber = dto.DeviceSerialNumber;
            existing.MinHeartRate = dto.MinHeartRate;
            existing.MaxHeartRate = dto.MaxHeartRate;
            existing.MinTemperature = dto.MinTemperature;
            existing.MaxTemperature = dto.MaxTemperature;
            existing.UpdateFrequency = dto.UpdateFrequency;
            existing.UpdatedAt = DateTime.UtcNow;

            await _monitoredService.UpdateMonitoredPersonAsync(existing);

            return Ok(new { Message = "Monitored person updated successfully.", MonitoredPerson = existing });
        }

        private static bool IsAdminRole(string? role)
        {
            return !string.IsNullOrWhiteSpace(role) && role.IndexOf("admin", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}