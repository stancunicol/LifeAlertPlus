using LifeAlertPlus.Shared.DTOs.Requests.Monitored;
using LifeAlertPlus.Shared.DTOs.Requests.User;
using LifeAlertPlus.Application.IServices;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MonitoredController : ControllerBase
    {
        private readonly IMonitoredService _monitoredService;
        private readonly IUserMonitoredService _userMonitoredService;
        private readonly IUserService _userService;

        public MonitoredController(IMonitoredService monitoredService, IUserMonitoredService userMonitoredService, IUserService userService)
        {
            _monitoredService = monitoredService;
            _userMonitoredService = userMonitoredService;
            _userService = userService;
        }

        [HttpPost("add")]
        public async Task<IActionResult> AddMonitoredPerson([FromBody] MonitorAddRequestDTO newMonitored)
        {
            var newPerson = newMonitored.MonitoredPerson;

            if(newPerson == null)
            {
                return BadRequest(new { Message = "Invalid monitored person data." });
            }

            if(string.IsNullOrEmpty(newPerson.FirstName) || string.IsNullOrEmpty(newPerson.LastName) || 
            string.IsNullOrEmpty(newPerson.DeviceSerialNumber) || string.IsNullOrEmpty(newPerson.Address) ||
            string.IsNullOrEmpty(newPerson.Gender))
            {
                return BadRequest(new { Message = "All fields are required are required." });
            }

            var request = await _monitoredService.GetMonitoredPersonByDeviceSerialNumberAsync(newPerson.DeviceSerialNumber);
            if(request != null)
            {
                return Conflict(new { Message = "A monitored person with the same device serial number already exists." });
            }

            var createdPerson = await _monitoredService.AddMonitoredPersonAsync(newPerson);
            if (createdPerson == null)
            {
                return StatusCode(500, new { Message = "Failed to add monitored person." });
            }

            var currentUser = await _userService.GetUserByEmailAsync(newMonitored.CurrentUserEmail);
            if (currentUser == null)
            {
                return NotFound(new { Message = "Current user not found." });
            }

            await _userMonitoredService.AddMonitoredPersonToUserAsync(currentUser.Id, createdPerson.Id);

            return Ok(new { Message = "Monitored person added successfully.", MonitoredPerson = createdPerson });
        }

        [HttpGet("{deviceSerialNumber}")]
        public async Task<IActionResult> GetMonitoredPersonByDeviceSerialNumber([FromQuery] string deviceSerialNumber)
        {
            var monitoredPerson = await _monitoredService.GetMonitoredPersonByDeviceSerialNumberAsync(deviceSerialNumber);
            if (monitoredPerson == null)
            {
                return NotFound(new { Message = "Monitored person not found." });
            }

            return Ok(monitoredPerson);
        }

        [HttpGet("id/{id:guid}")]
        public async Task<IActionResult> GetMonitoredPersonById([FromQuery] Guid id)
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