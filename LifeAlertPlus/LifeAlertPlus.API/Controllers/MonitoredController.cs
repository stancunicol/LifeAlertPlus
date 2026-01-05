using LifeAlertPlus.Shared.DTOs.Requests.Monitored;
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

        public MonitoredController(IMonitoredService monitoredService)
        {
            _monitoredService = monitoredService;
        }

        [HttpPost("add")]
        public async Task<IActionResult> AddMonitoredPerson([FromBody] MonitorCreateRequestDTO newPerson)
        {
            if(newPerson == null)
            {
                return BadRequest(new { Message = "Invalid monitored person data." });
            }

            if(string.IsNullOrEmpty(newPerson.FirstName) || string.IsNullOrEmpty(newPerson.LastName) || 
            string.IsNullOrEmpty(newPerson.DeviceSerialNumber) || string.IsNullOrEmpty(newPerson.Address) || 
            newPerson.Birthdate == null || string.IsNullOrEmpty(newPerson.Gender))
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

            return Ok(new { Message = "Monitored person added successfully.", MonitoredPerson = createdPerson });
        }

        [HttpGet("{deviceSerialNumber}")]
        public async Task<IActionResult> GetMonitoredPersonByDeviceSerialNumber(string deviceSerialNumber)
        {
            var monitoredPerson = await _monitoredService.GetMonitoredPersonByDeviceSerialNumberAsync(deviceSerialNumber);
            if (monitoredPerson == null)
            {
                return NotFound(new { Message = "Monitored person not found." });
            }

            return Ok(monitoredPerson);
        }
    }
}