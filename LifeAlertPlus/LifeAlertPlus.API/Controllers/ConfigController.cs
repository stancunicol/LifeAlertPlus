using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class ConfigController : BaseApiController
    {
        private readonly IConfiguration _configuration;

        public ConfigController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [HttpGet("maps")]
        public IActionResult GetMapsKey()
        {
            var key = _configuration["GoogleMaps:ApiKey"];
            if (string.IsNullOrWhiteSpace(key))
                return NotFound(new { message = "Maps API key not configured." });

            return Ok(new { apiKey = key });
        }
    }
}
