using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using LifeAlertPlus.API.Services;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Authorize(Roles = "Admin")]
    [Route("api/[controller]")]
    public class SimulationsController : ControllerBase
    {
        private readonly SimulationManager _simulationManager;
        private readonly ILogger<SimulationsController> _logger;

        public SimulationsController(SimulationManager simulationManager, ILogger<SimulationsController> logger)
        {
            _simulationManager = simulationManager;
            _logger = logger;
        }

        /// <summary>
        /// Start simulation for a specific monitored person
        /// </summary>
        [HttpPost("start/{personId}")]
        public async Task<IActionResult> Start(Guid personId)
        {
            if (personId == Guid.Empty)
            {
                return BadRequest(new { success = false, message = "Invalid person ID" });
            }

            _logger.LogInformation("Received request to start simulation for person {PersonId}", personId);
            await _simulationManager.StartSimulationAsync(personId);
            return Ok(new { success = true, message = "Simulation started" });
        }

        /// <summary>
        /// Start simulations for all monitored persons
        /// </summary>
        [HttpPost("startAll")]
        public async Task<IActionResult> StartAll()
        {
            _logger.LogInformation("Received request to start all simulations");
            await _simulationManager.StartAllAsync();
            return Ok(new { success = true, message = "All simulations started" });
        }

        /// <summary>
        /// Stop simulation for a specific monitored person
        /// </summary>
        [HttpPost("stop/{personId}")]
        public async Task<IActionResult> Stop(Guid personId)
        {
            if (personId == Guid.Empty)
            {
                return BadRequest(new { success = false, message = "Invalid person ID" });
            }

            _logger.LogInformation("Received request to stop simulation for person {PersonId}", personId);
            await _simulationManager.StopSimulationAsync(personId);
            return Ok(new { success = true, message = "Simulation stopped" });
        }

        /// <summary>
        /// Stop all running simulations
        /// </summary>
        [HttpPost("stopAll")]
        public async Task<IActionResult> StopAll()
        {
            _logger.LogInformation("Received request to stop all simulations");
            await _simulationManager.StopAllAsync();
            return Ok(new { success = true, message = "All simulations stopped" });
        }

        /// <summary>
        /// Seed today's measurements (one per 30 min from midnight to now) for chart population
        /// </summary>
        [HttpPost("seedToday/{personId}")]
        public async Task<IActionResult> SeedToday(Guid personId)
        {
            if (personId == Guid.Empty)
                return BadRequest(new { success = false, message = "Invalid person ID" });

            await _simulationManager.SeedTodayAsync(personId);
            return Ok(new { success = true, message = "Today's seed data generated" });
        }

        /// <summary>
        /// Force reseed today — deletes existing zero-SpO2 measurements for today then reseeds
        /// </summary>
        [HttpPost("reseedToday/{personId}")]
        public async Task<IActionResult> ReseedToday(Guid personId)
        {
            if (personId == Guid.Empty)
                return BadRequest(new { success = false, message = "Invalid person ID" });

            await _simulationManager.ReseedTodayAsync(personId);
            return Ok(new { success = true, message = "Today's data reseeded with SpO2" });
        }

        /// <summary>
        /// Get list of currently running simulation IDs
        /// </summary>
        [HttpGet("running")]
        public IActionResult GetRunning()
        {
            var ids = _simulationManager.GetRunningPersonIds();
            _logger.LogDebug("Returning {Count} running simulation IDs", ids.Count());
            return Ok(ids);
        }
    }
}
