using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LifeAlertPlus.Shared.DTOs.Requests.AI;
using LifeAlertPlus.Shared.DTOs.Responses.AI;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Infrastructure.Context;
using System.Text.Json;

namespace LifeAlertPlus.API.Controllers
{
    // Controller proxy pentru microserviciul AI de predicție a tendințelor vitale.
    // Microserviciul AI (Python/Flask, rulează separat pe portul 8000) primește datele senzorilor
    // și returnează predicții: va crește/scădea pulsul? Există risc de deteriorare?
    // Rolul acestui controller este să îmbogățească cererea cu condițiile medicale și pragurile
    // personalizate ale pacientului ÎNAINTE de a trimite datele la microserviciu.
    [ApiController]
    [Authorize] // Necesită autentificare
    [Route("api/[controller]")]
    public class AIController(
        IHttpClientFactory httpClientFactory,
        ILogger<AIController> logger,
        IServiceScopeFactory scopeFactory) : ControllerBase
    {
        // Opțiuni JSON: snake_case la serializare (compatibil cu API-ul Python), case-insensitive la deserializare
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower // "pulse_rate" în loc de "PulseRate"
        };

        // POST /api/ai/predict — Trimite datele senzorilor la microserviciul AI și returnează predicțiile
        // Îmbogățește cererea cu bolile pacientului și pragurile personalizate pentru predicții mai precise
        [HttpPost("predict")]
        public async Task<IActionResult> Predict([FromBody] AIPredictionRequestDTO request)
        {
            if (request == null)
                return BadRequest(new { Message = "Invalid prediction request." });

            // Variabile pentru condițiile și pragurile specifice pacientului
            List<string> conditions = new(); // Ex: ["hypertension", "arrhythmia"]
            int? maxHr = null, minHr = null;
            double? maxTemp = null, minTemp = null;
            int? minSpO2 = null, maxSpO2 = null;

            // Dacă cererea conține ID-ul persoanei monitorizate, încărcăm profilul medical din DB
            if (request.MonitoredId.HasValue && request.MonitoredId != Guid.Empty)
            {
                try
                {
                    // Folosim IServiceScopeFactory deoarece AIController poate fi Transient/Scoped
                    using var scope = scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<LifeAlertPlusDbContext>();
                    var condRepo = scope.ServiceProvider.GetRequiredService<IMonitoredConditionRepository>();

                    // Citim pragurile personalizate ale pacientului (setate de utilizator sau calculate din boli)
                    var monitored = await db.Monitoreds.FindAsync(request.MonitoredId.Value);
                    if (monitored != null)
                    {
                        maxHr   = monitored.MaxHeartRate;
                        minHr   = monitored.MinHeartRate;
                        maxTemp = monitored.MaxTemperature;
                        minTemp = monitored.MinTemperature;
                        minSpO2 = monitored.MinSpO2;
                        maxSpO2 = monitored.MaxSpO2;
                    }

                    // Citim lista de boli diagnosticate ale pacientului pentru context AI
                    var conds = await condRepo.GetByMonitoredIdAsync(request.MonitoredId.Value);
                    conditions = conds.Select(c => c.ConditionKey).ToList();
                }
                catch (Exception ex)
                {
                    // Logăm avertismentul dar continuăm cu predicția fără date personalizate
                    logger.LogWarning(ex, "Failed to load conditions/thresholds for MonitoredId {MonitoredId}. Using defaults.", request.MonitoredId);
                }
            }

            try
            {
                // Clientul HTTP "AiService" este preconfigurat cu BaseAddress = URL-ul microserviciului AI
                var client = httpClientFactory.CreateClient("AiService");

                // Construim payload-ul complet pentru microserviciul AI
                // Includeau datele senzorilor + condițiile medicale + pragurile personalizate
                var payload = new
                {
                    pulse            = request.Pulse,        // Pulsul curent (bpm)
                    temperature      = request.Temperature,  // Temperatura corporală (°C)
                    spo2             = request.Spo2,          // Saturația oxigenului (%)
                    accel_x          = request.AccelX,        // Accelerometru MPU6050 — axa X
                    accel_y          = request.AccelY,        // Accelerometru MPU6050 — axa Y
                    accel_z          = request.AccelZ,        // Accelerometru MPU6050 — axa Z
                    gyro_x           = request.GyroX,         // Giroscop MPU6050 — axa X
                    gyro_y           = request.GyroY,         // Giroscop MPU6050 — axa Y
                    gyro_z           = request.GyroZ,         // Giroscop MPU6050 — axa Z
                    conditions,                               // Bolile pacientului pentru context clinic
                    max_heart_rate   = maxHr,                // Pragul maxim personalizat de puls
                    min_heart_rate   = minHr,                // Pragul minim personalizat de puls
                    max_temperature  = maxTemp,              // Pragul maxim personalizat de temperatură
                    min_temperature  = minTemp,              // Pragul minim personalizat de temperatură
                    min_spo2         = minSpO2,              // Pragul minim personalizat de SpO2
                    max_spo2         = maxSpO2,              // Pragul maxim personalizat de SpO2 (de obicei 100%)
                };

                var response = await client.PostAsJsonAsync("/predict", payload); // Apel HTTP POST la microserviciu

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("AI service returned {StatusCode}", (int)response.StatusCode);
                    return StatusCode(502, new { Message = "AI service unavailable." }); // 502 Bad Gateway
                }

                var json = await response.Content.ReadAsStringAsync();
                // Deserializăm răspunsul Python (snake_case) în DTO C# (PascalCase)
                var prediction = JsonSerializer.Deserialize<AIPredictionResponseDTO>(json, _jsonOptions);

                if (prediction == null)
                    return StatusCode(502, new { Message = "Invalid AI service response." });

                return Ok(prediction); // Returnăm predicția direct clientului Blazor
            }
            catch (HttpRequestException ex)
            {
                // Microserviciul AI nu rulează sau nu e accesibil (ex. pornire)
                logger.LogWarning(ex, "AI service connection failed");
                return StatusCode(502, new { Message = "AI service unreachable." });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error calling AI service");
                return StatusCode(500, new { Message = "Error processing AI prediction." });
            }
        }

        // GET /api/ai/health — Verifică dacă microserviciul AI rulează (proxiază răspunsul /health al lui)
        // Folosit de UI pentru a afișa starea microserviciului în panoul de admin
        [HttpGet("health")]
        [AllowAnonymous] // Accesibil fără token pentru a putea fi folosit în health check-uri externe
        public async Task<IActionResult> Health()
        {
            try
            {
                var client = httpClientFactory.CreateClient("AiService");
                var response = await client.GetAsync("/health"); // Forwardăm cererea la microserviciu
                var json = await response.Content.ReadAsStringAsync();
                return Content(json, "application/json"); // Returnăm răspunsul nemodicat
            }
            catch (Exception ex)
            {
                // Microserviciul nu răspunde
                logger.LogWarning(ex, "AI health check failed");
                return Ok(new { status = "unavailable" }); // Răspuns standard pentru UI
            }
        }
    }
}
