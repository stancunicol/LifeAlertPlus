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
