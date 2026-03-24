using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using LifeAlertPlus.API.Services;
using System.Threading.Tasks;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Authorize(Roles = "Admin")]
    [Route("api/[controller]")]
    public class SimulationsController : ControllerBase
    {
        private readonly SimulationManager _simulationManager;

        public SimulationsController(SimulationManager simulationManager)
        {
            _simulationManager = simulationManager;
        }

        [HttpPost("start/{personId}")]
        public async Task<IActionResult> Start(Guid personId)
        {
            await _simulationManager.StartSimulationAsync(personId);
            return Ok();
        }

        [HttpPost("startAll")]
        public async Task<IActionResult> StartAll()
        {
            await _simulationManager.StartAllAsync();
            return Ok();
        }

        [HttpPost("stop/{personId}")]
        public async Task<IActionResult> Stop(Guid personId)
        {
            await _simulationManager.StopSimulationAsync(personId);
            return Ok();
        }

        [HttpPost("stopAll")]
        public async Task<IActionResult> StopAll()
        {
            await _simulationManager.StopAllAsync();
            return Ok();
        }

        [AllowAnonymous]
        [HttpGet("running")]
        public IActionResult GetRunning()
        {
            var ids = _simulationManager.GetRunningPersonIds();
            return Ok(ids);
        }
    }
}
