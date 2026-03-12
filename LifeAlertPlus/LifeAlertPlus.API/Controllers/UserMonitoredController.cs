using LifeAlertPlus.Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class UserMonitoredController : ControllerBase
    {
        private readonly IUserMonitoredService _userMonitoredService;

        public UserMonitoredController(IUserMonitoredService userMonitoredService)
        {
            _userMonitoredService = userMonitoredService;
        }

        private bool CallerOwns(Guid userId)
        {
            var callerIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return callerIdStr != null && Guid.TryParse(callerIdStr, out var callerGuid) && callerGuid == userId;
        }

        [HttpGet("{userId}/monitored")]
        public async Task<IActionResult> GetMonitoredPeopleByUserId(Guid userId)
        {
            if (!CallerOwns(userId))
                return Forbid();

            var monitoredPeople = await _userMonitoredService.GetMonitoredPeopleByUserIdAsync(userId);
            return Ok(monitoredPeople);
        }

        [HttpPost("{userId}/monitored/{monitoredPersonId}")]
        public async Task<IActionResult> AddMonitoredPersonToUser(Guid userId, Guid monitoredPersonId)
        {
            if (!CallerOwns(userId))
                return Forbid();

            await _userMonitoredService.AddMonitoredPersonToUserAsync(userId, monitoredPersonId);
            return NoContent();
        }
    }
}