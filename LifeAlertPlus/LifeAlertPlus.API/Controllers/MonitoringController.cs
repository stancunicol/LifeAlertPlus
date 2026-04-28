using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LifeAlertPlus.API.Services;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class MonitoringController : ControllerBase
    {
        private readonly AlertMonitorService _alertMonitorService;

        public MonitoringController(AlertMonitorService alertMonitorService)
        {
            _alertMonitorService = alertMonitorService;
        }

        [HttpGet("{monitoredId:guid}/predictions")]
        public IActionResult GetTrendPredictions(Guid monitoredId)
        {
            var result = _alertMonitorService.GetTrendPredictions(monitoredId);
            return Ok(result);
        }
    }
}
