using LifeAlertPlus.API.Services;
using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Shared.DTOs.Responses.ActivityProfile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ActivityProfileController : ControllerBase
    {
        private readonly ActivityProfileService _activityProfileService;
        private readonly IUserMonitoredService _userMonitoredService;

        public ActivityProfileController(ActivityProfileService activityProfileService, IUserMonitoredService userMonitoredService)
        {
            _activityProfileService = activityProfileService;
            _userMonitoredService = userMonitoredService;
        }

        private async Task<bool> UserOwnsMonitoredAsync(Guid monitoredId)
        {
            var callerIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(callerIdStr, out var callerId)) return false;
            var owned = await _userMonitoredService.GetMonitoredPeopleByUserIdAsync(callerId);
            return owned.Any(m => m.Id == monitoredId);
        }

        [HttpGet("{monitoredId}")]
        public async Task<IActionResult> GetProfile(Guid monitoredId)
        {
            if (monitoredId == Guid.Empty)
                return BadRequest(new { Message = "Invalid monitored ID." });

            if (!await UserOwnsMonitoredAsync(monitoredId))
                return Forbid();

            var profiles = await _activityProfileService.GetProfileAsync(monitoredId);

            if (profiles.Count == 0)
                return NotFound(new { Message = "No activity profile found. A build may be in progress." });

            var response = new ActivityProfileResponseDTO
            {
                IdMonitored = monitoredId,
                LastUpdated = profiles.Max(p => p.LastUpdated),
                HourlyProfiles = profiles.Select(p => new HourlyProfileDTO
                {
                    HourOfDay = p.HourOfDay,
                    AveragePulse = p.AveragePulse,
                    MovementRate = p.MovementRate,
                    SleepProbability = p.SleepProbability,
                    DataPoints = p.DataPoints,
                    Label = GetLabel(p.MovementRate, p.SleepProbability, p.DataPoints)
                }).ToList()
            };

            return Ok(response);
        }

        [HttpPost("{monitoredId}/build")]
        public async Task<IActionResult> TriggerBuild(Guid monitoredId)
        {
            if (monitoredId == Guid.Empty)
                return BadRequest(new { Message = "Invalid monitored ID." });

            if (!await UserOwnsMonitoredAsync(monitoredId))
                return Forbid();

            _ = Task.Run(() => _activityProfileService.BuildProfileAsync(monitoredId));

            return Accepted(new { Message = "Profile build started." });
        }

        private static string GetLabel(double movementRate, double sleepProbability, int dataPoints)
        {
            if (dataPoints < 10) return "Date insuficiente";
            if (sleepProbability > 0.60) return "Somn";
            if (movementRate > 0.60) return "Activ";
            if (movementRate > 0.30) return "Moderat activ";
            return "Inactiv / Odihnă";
        }
    }
}
