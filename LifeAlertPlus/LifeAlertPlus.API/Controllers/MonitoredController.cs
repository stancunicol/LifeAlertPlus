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

            var createdPerson = await _monitoredService.AddMonitoredPersonAsync(newPerson);
            if (createdPerson == null)
            {
                return StatusCode(500, new { Message = "Failed to add monitored person." });
            }

            await _userMonitoredService.AddMonitoredPersonToUserAsync(callerId, createdPerson.Id);

            return Ok(new { Message = "Monitored person added successfully.", MonitoredPerson = createdPerson });
        }

        [HttpGet("serial/{deviceSerialNumber}")]
        public async Task<IActionResult> GetMonitoredPersonByDeviceSerialNumber([FromRoute] string deviceSerialNumber)
        {
            var monitoredPerson = await _monitoredService.GetMonitoredPersonByDeviceSerialNumberAsync(deviceSerialNumber);
            if (monitoredPerson == null)
            {
                return NotFound(new { Message = "Monitored person not found." });
            }

            return Ok(monitoredPerson);
        }

        [HttpGet("id/{id:guid}")]
        public async Task<IActionResult> GetMonitoredPersonById([FromRoute] Guid id)
        {
            var monitoredPerson = await _monitoredService.GetMonitoredPersonByIdAsync(id);
            if (monitoredPerson == null)
            {
                return NotFound(new { Message = "Monitored person not found." });
            }

            return Ok(monitoredPerson);
        }
    }
}