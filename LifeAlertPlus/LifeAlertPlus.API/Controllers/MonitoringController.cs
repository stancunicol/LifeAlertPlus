using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LifeAlertPlus.API.Services;
using LifeAlertPlus.Application.IServices;
using System.Security.Claims;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class MonitoringController : ControllerBase
    {
        private readonly AlertMonitorService _alertMonitorService;
        private readonly IUserMonitoredService _userMonitoredService;

        public MonitoringController(AlertMonitorService alertMonitorService, IUserMonitoredService userMonitoredService)
        {
            _alertMonitorService = alertMonitorService;
            _userMonitoredService = userMonitoredService;
        }

        [HttpGet("{monitoredId:guid}/predictions")]
        public async Task<IActionResult> GetTrendPredictions(Guid monitoredId)
        {
            var callerIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(callerIdStr, out var callerId))
                return Unauthorized();

            var owned = await _userMonitoredService.GetMonitoredPeopleByUserIdAsync(callerId);
            if (!owned.Any(m => m.Id == monitoredId))
                return Forbid();

            var result = _alertMonitorService.GetTrendPredictions(monitoredId);
            return Ok(result);
        }
    }
}
