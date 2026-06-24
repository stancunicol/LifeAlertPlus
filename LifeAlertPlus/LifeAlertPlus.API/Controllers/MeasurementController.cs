using LifeAlertPlus.Shared.DTOs.Requests.Measurement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.API.Controllers
{
    // Controller pentru gestionarea măsurătorilor vitale (puls, temperatură, SpO2)
    // Permite adăugarea manuală de măsurători (de la utilizator) și interogarea istoricului
    [ApiController]
    [Authorize] // Toate endpoint-urile necesită autentificare JWT
    [Route("api/[controller]")] // URL de bază: /api/measurement
    public class MeasurementController : BaseApiController
    {
        private readonly IMeasurementService _measurementService;
        private readonly Services.AlertMonitorService _alertMonitor; // Pentru evaluarea alertelor în timp real
        private readonly IUserMonitoredService _userMonitoredService; // Verifică dacă utilizatorul are acces la persoana monitorizată
        private readonly ILogger<MeasurementController> _logger;

        public MeasurementController(IMeasurementService measurementService, Services.AlertMonitorService alertMonitor, IUserMonitoredService userMonitoredService, ILogger<MeasurementController> logger)
        {
            _measurementService = measurementService;
            _alertMonitor = alertMonitor;
            _userMonitoredService = userMonitoredService;
            _logger = logger;
        }

        // Verifică dacă utilizatorul curent are dreptul să acceseze datele unei persoane monitorizate
        // Admin-ul are acces la toate; utilizatorul normal doar la persoanele lui
        private async Task<bool> UserOwnsMonitoredAsync(Guid monitoredId)
        {
            if (IsAdminRole()) return true; // Adminul nu are restricții
            var callerId = GetCallerId(); // ID-ul utilizatorului din token JWT
            if (callerId == null) return false;
            var owned = await _userMonitoredService.GetMonitoredPeopleByUserIdAsync(callerId.Value);
            return owned.Any(m => m.Id == monitoredId); // Verificăm că persoana e în lista sa
        }

        // POST /api/measurement — Adaugă o măsurătoare manuală
        // Folosit când utilizatorul introduce date manual (nu de la ESP)
        [HttpPost]
        public async Task<IActionResult> AddMeasurement([FromBody] MeasurementRequestDTO measurementDto)
        {
            if(measurementDto == null)
                return BadRequest(new { Message = "Invalid measurement data." });

            // Validăm câmpurile obligatorii
            if(string.IsNullOrWhiteSpace(measurementDto.Name) || string.IsNullOrWhiteSpace(measurementDto.Activity) || string.IsNullOrWhiteSpace(measurementDto.Coordinates))
                return BadRequest(new { Message = "Name, Activity and Coordinates are required." });

            // Validăm că valorile vitale sunt pozitive (valori 0 sau negative indică eroare de senzor)
            if(measurementDto.Pulse <= 0 || measurementDto.Temperature <= 0)
                return BadRequest(new { Message = "Pulse and Temperature must be greater than zero." });

            if(measurementDto.IdMonitored == Guid.Empty)
                return BadRequest(new { Message = "IdMonitored is required." });

            // Verificăm că utilizatorul are dreptul să adauge date pentru această persoană
            if (!await UserOwnsMonitoredAsync(measurementDto.IdMonitored))
                return Forbid(); // 403 Forbidden

            // Construim entitatea de măsurătoare cu ID nou generat și timestamp curent
            var measurement = new Measurement
            {
                Id = Guid.NewGuid(),
                Name = measurementDto.Name,
                Activity = measurementDto.Activity,
                IsFall = measurementDto.IsFall,
                IdMonitored = measurementDto.IdMonitored,
                Pulse = measurementDto.Pulse,
                Temperature = measurementDto.Temperature,
                SpO2 = measurementDto.SpO2,
                Coordinates = measurementDto.Coordinates,
                CreatedAt = DateTime.UtcNow // Întotdeauna ora serverului în UTC
            };

            await _measurementService.AddMeasurementAsync(measurement); // Persistăm în DB

            // Feed the measurement to the alert monitor for sustained-alert detection
            // Procesăm în background — nu blocăm răspunsul HTTP pentru evaluarea alertelor
            var monitoredId = measurementDto.IdMonitored;
            _ = Task.Run(async () =>
            {
                try
                {
                    // Trimitem datele la sistemul de monitorizare pentru a verifica dacă trebuie alertă
                    await _alertMonitor.ProcessMeasurementAsync(
                        monitoredId,
                        measurementDto.Pulse,
                        measurementDto.Temperature,
                        measurementDto.SpO2,
                        measurementDto.IsFall,
                        measurementDto.Activity,
                        measurementDto.Coordinates);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "ProcessMeasurementAsync failed for monitored {MonitoredId}", monitoredId);
                }
            });

            return Ok(new { Message = "Measurement added successfully." });
        }

        // GET /api/measurement/monitored/{idMonitored}?pageNumber=1&pageSize=10 — Istoricul măsurătorilor
        // Returnează paginat toate măsurătorile pentru o persoană monitorizată
        [HttpGet("monitored/{idMonitored}")]
        public async Task<IActionResult> GetMeasurementsByMonitoredId(Guid idMonitored, int pageNumber = 1, int pageSize = 10)
        {
            if(idMonitored == Guid.Empty)
                return BadRequest(new { Message = "Invalid monitored ID." });

            // Verificăm accesul
            if (!await UserOwnsMonitoredAsync(idMonitored))
                return Forbid();

            // Sanitizăm parametrii de paginare (prevenim valori invalide)
            pageNumber = Math.Max(1, pageNumber); // Minim pagina 1
            pageSize = Math.Clamp(pageSize, 1, 10000); // Între 1 și 10.000 înregistrări per pagină
            var measurements = await _measurementService.GetMeasurementsByMonitoredIdAsync(idMonitored, pageNumber, pageSize);
            return Ok(measurements);
        }

        // GET /api/measurement/{id} — O singură măsurătoare după ID
        [HttpGet("{id}")]
        public async Task<IActionResult> GetMeasurementById(Guid id)
        {
            if(id == Guid.Empty)
                return BadRequest(new { Message = "Invalid measurement ID." });

            var measurement = await _measurementService.GetMeasurementByIdAsync(id);
            if (measurement == null)
                return NotFound(new { Message = "Measurement not found." });

            // Verificăm că utilizatorul are acces la persoana căreia îi aparține măsurătoarea
            if (!await UserOwnsMonitoredAsync(measurement.IdMonitored))
                return Forbid();

            return Ok(measurement);
        }

        // GET /api/measurement/today/count — Numărul total de măsurători înregistrate azi
        // Folosit pe dashboard-ul de admin pentru statistici
        [HttpGet("today/count")]
        public async Task<IActionResult> GetTodayMeasurementsCount()
        {
            var count = await _measurementService.GetTodayMeasurementsCountAsync();
            return Ok(new { Count = count });
        }
    }
}
