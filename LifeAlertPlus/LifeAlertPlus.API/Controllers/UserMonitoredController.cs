using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Shared.DTOs.Requests.Monitored;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UserMonitoredController : ControllerBase
    {
        private readonly IUserMonitoredService _userMonitoredService;

        public UserMonitoredController(IUserMonitoredService userMonitoredService)
        {
            _userMonitoredService = userMonitoredService;
        }

        [HttpGet("{userId}/monitored")]
        public async Task<IActionResult> GetMonitoredPeopleByUserId(Guid userId)
        {
            var monitoredPeople = await _userMonitoredService.GetMonitoredPeopleByUserIdAsync(userId);
            return Ok(monitoredPeople);
        }

        [HttpPost("{userId}/monitored/{monitoredPersonId}")]
        public async Task<IActionResult> AddMonitoredPersonToUser(Guid userId, Guid monitoredPersonId)
        {
            await _userMonitoredService.AddMonitoredPersonToUserAsync(userId, monitoredPersonId);
            return NoContent();
        }
    }
}