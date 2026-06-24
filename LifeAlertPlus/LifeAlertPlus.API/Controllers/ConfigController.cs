using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace LifeAlertPlus.API.Controllers
{
    // Controller pentru expunerea configurațiilor publice necesare clientului Blazor.
    // Cheile API nu pot fi incluse direct în Blazor WASM (codul e vizibil în browser),
    // deci sunt returnate la cerere după autentificare, astfel limitând expunerea lor.
    [ApiController]
    [Authorize] // Necesită autentificare — cheile nu sunt disponibile fără token JWT
    [Route("api/[controller]")]
    public class ConfigController : BaseApiController
    {
        private readonly IConfiguration _configuration; // Accesează appsettings.json

        public ConfigController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // GET /api/config/maps — Returnează cheia API Google Maps pentru afișarea hărților
        // (localizare GPS pacient, cel mai apropiat spital)
        [HttpGet("maps")]
        public IActionResult GetMapsKey()
        {
            var key = _configuration["GoogleMaps:ApiKey"]; // Citită din appsettings.json sau variabilă de mediu
            if (string.IsNullOrWhiteSpace(key))
                return NotFound(new { message = "Maps API key not configured." }); // Admin nu a configurat cheia

            return Ok(new { apiKey = key }); // Trimisă clientului pentru a inițializa componenta hartă
        }
    }
}
