using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Shared.DTOs.Responses.ESP;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class ImportController : BaseApiController
    {
        private readonly IImportService _importService;
        private readonly IMonitoredService _monitoredService;
        private readonly IUserMonitoredService _userMonitoredService;

        public ImportController(IImportService importService, IMonitoredService monitoredService, IUserMonitoredService userMonitoredService)
        {
            _importService = importService;
            _monitoredService = monitoredService;
            _userMonitoredService = userMonitoredService;
        }

        [HttpPost("esp-data")]
        [RequestSizeLimit(1_048_576)]
        public async Task<IActionResult> ImportESPData([FromBody] string jsonContent)
        {
            var result = await _importService.ImportAndValidateAsync<ESPDataResponseDTO>(jsonContent);
            if (!result.Success)
                return BadRequest(new { Errors = result.Errors });

            // Verify caller owns every monitored person referenced by serial in the payload
            var callerId = GetCallerId();
            if (callerId == null)
                return Unauthorized();

            var ownedMonitoreds = await _userMonitoredService.GetMonitoredPeopleByUserIdAsync(callerId.Value);
            var ownedSerials = new HashSet<string>(
                ownedMonitoreds.Select(m => m.DeviceSerialNumber),
                StringComparer.OrdinalIgnoreCase);

            var unauthorizedSerials = result.Data!
                .Where(d => !string.IsNullOrWhiteSpace(d.Serial) && !ownedSerials.Contains(d.Serial))
                .Select(d => d.Serial)
                .Distinct()
                .ToList();

            if (unauthorizedSerials.Count > 0)
                return Forbid();

            return Ok(new { Message = "Import reușit.", Data = result.Data });
        }
    }
}
