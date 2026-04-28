using LifeAlertPlus.API.Services;
using LifeAlertPlus.Shared.DTOs.Responses.ActivityProfile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ActivityProfileController : ControllerBase
    {
        private readonly ActivityProfileService _activityProfileService;

        public ActivityProfileController(ActivityProfileService activityProfileService)
        {
            _activityProfileService = activityProfileService;
        }

        [HttpGet("{monitoredId}")]
        public async Task<IActionResult> GetProfile(Guid monitoredId)
        {
            if (monitoredId == Guid.Empty)
                return BadRequest(new { Message = "Invalid monitored ID." });

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
        public IActionResult TriggerBuild(Guid monitoredId)
        {
            if (monitoredId == Guid.Empty)
                return BadRequest(new { Message = "Invalid monitored ID." });

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
