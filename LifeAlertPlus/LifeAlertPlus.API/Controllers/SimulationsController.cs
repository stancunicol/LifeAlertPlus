using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using LifeAlertPlus.API.Services;

namespace LifeAlertPlus.API.Controllers
{
    // Controller pentru controlul simulărilor ESP — permite admins să pornească/oprească
    // generarea automată de date vitale fără dispozitiv fizic real.
    // Util în development, demo și testare fără ESP32 conectat.
    // Acces EXCLUSIV pentru administratori (Roles = "Admin").
    [ApiController]
    [Authorize(Roles = "Admin")] // Numai adminii pot controla simulările
    [Route("api/[controller]")]
    public class SimulationsController : ControllerBase
    {
        private readonly SimulationManager _simulationManager; // Gestionează loop-urile de simulare per persoană
        private readonly ILogger<SimulationsController> _logger;

        public SimulationsController(SimulationManager simulationManager, ILogger<SimulationsController> logger)
        {
            _simulationManager = simulationManager;
            _logger = logger;
        }

        // POST /api/simulations/start/{personId} — Pornește simularea pentru o persoană monitorizată
        // Loop-ul generează date vitale la fiecare 30 de secunde și le injectează prin ESPController
        [HttpPost("start/{personId}")]
        public async Task<IActionResult> Start(Guid personId)
        {
            if (personId == Guid.Empty)
                return BadRequest(new { success = false, message = "Invalid person ID" });

            _logger.LogInformation("Received request to start simulation for person {PersonId}", personId);
            await _simulationManager.StartSimulationAsync(personId);
            return Ok(new { success = true, message = "Simulation started" });
        }

        // POST /api/simulations/startAll — Pornește simulările pentru TOATE persoanele monitorizate
        // Util pentru demo sau popularea inițială a bazei de date cu date de test
        [HttpPost("startAll")]
        public async Task<IActionResult> StartAll()
        {
            _logger.LogInformation("Received request to start all simulations");
            await _simulationManager.StartAllAsync();
            return Ok(new { success = true, message = "All simulations started" });
        }

        // POST /api/simulations/stop/{personId} — Oprește simularea pentru o persoană monitorizată
        // Anulează CancellationToken-ul loop-ului și așteaptă terminarea graceful (max 5 sec)
        [HttpPost("stop/{personId}")]
        public async Task<IActionResult> Stop(Guid personId)
        {
            if (personId == Guid.Empty)
                return BadRequest(new { success = false, message = "Invalid person ID" });

            _logger.LogInformation("Received request to stop simulation for person {PersonId}", personId);
            await _simulationManager.StopSimulationAsync(personId);
            return Ok(new { success = true, message = "Simulation stopped" });
        }

        // POST /api/simulations/stopAll — Oprește toate simulările active
        [HttpPost("stopAll")]
        public async Task<IActionResult> StopAll()
        {
            _logger.LogInformation("Received request to stop all simulations");
            await _simulationManager.StopAllAsync();
            return Ok(new { success = true, message = "All simulations stopped" });
        }

        // POST /api/simulations/seedToday/{personId} — Populează istoricul de azi (o citire la 30 min)
        // Permite vizualizarea graficelor de azi fără a fi nevoie ca simularea să ruleze ore întregi
        [HttpPost("seedToday/{personId}")]
        public async Task<IActionResult> SeedToday(Guid personId)
        {
            if (personId == Guid.Empty)
                return BadRequest(new { success = false, message = "Invalid person ID" });

            await _simulationManager.SeedTodayAsync(personId);
            return Ok(new { success = true, message = "Today's seed data generated" });
        }

        // POST /api/simulations/reseedToday/{personId} — Forțează re-popularea de azi
        // Șterge mai întâi măsurătorile de azi cu SpO2=0 (datele incomplete din seed anterior),
        // apoi le regenerează complet cu toate câmpurile valide
        [HttpPost("reseedToday/{personId}")]
        public async Task<IActionResult> ReseedToday(Guid personId)
        {
            if (personId == Guid.Empty)
                return BadRequest(new { success = false, message = "Invalid person ID" });

            await _simulationManager.ReseedTodayAsync(personId);
            return Ok(new { success = true, message = "Today's data reseeded with SpO2" });
        }

        // GET /api/simulations/running — Returnează lista ID-urilor persoanelor cu simulare activă
        // Folosit de panoul de admin pentru a afișa starea curentă a simulărilor
        [HttpGet("running")]
        public IActionResult GetRunning()
        {
            var ids = _simulationManager.GetRunningPersonIds(); // ID-urile din ConcurrentDictionary
            _logger.LogDebug("Returning {Count} running simulation IDs", ids.Count());
            return Ok(ids);
        }
    }
}
