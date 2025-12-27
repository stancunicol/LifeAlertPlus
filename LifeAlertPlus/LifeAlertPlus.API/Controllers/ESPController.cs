using System.Net.Http;
using System.Text.Json;
using System.Threading;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using LifeAlertPlus.Shared.DTOs.Responses.ESP;

namespace ESPController
{
    [ApiController]
    [Route("api/[controller]")]
    public class ESPController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public ESPController(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }
        /// <summary>
        /// Get ESP device data by serial number.
        /// </summary>
        /// <param name="serial">Serial number of the ESP device</param>
        /// <returns>ESP device data in the required format</returns>
        [HttpGet("data/{serial}")]
        public async Task<IActionResult> GetESPData(string serial, CancellationToken cancellationToken)
        {
            var client = _httpClientFactory.CreateClient();
            var url = $"http://localhost:5000/api/data/{serial}";
            try
            {
                var response = await client.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return StatusCode((int)response.StatusCode, $"Failed to fetch data for serial {serial}");
                }
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var data = JsonSerializer.Deserialize<ESPDataResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (data == null)
                {
                    return BadRequest("Invalid data format received.");
                }
                return Ok(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error fetching data: {ex.Message}");
            }
        }
    }

    public class ActivateRequest
    {
        public string DeviceId { get; set; }
        public string UserId { get; set; }
    }
}