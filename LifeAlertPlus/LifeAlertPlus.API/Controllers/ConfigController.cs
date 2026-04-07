using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ConfigController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public ConfigController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [HttpGet("maps")]
        [AllowAnonymous]
        public IActionResult GetMapsKey()
        {
            var key = _configuration["GoogleMaps:ApiKey"];
            if (string.IsNullOrWhiteSpace(key))
                return NotFound(new { message = "Maps API key not configured." });

            return Ok(new { apiKey = key });
        }
    }
}
