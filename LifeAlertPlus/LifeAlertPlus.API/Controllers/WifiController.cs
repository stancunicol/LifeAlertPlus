using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Shared.DTOs.Requests.Wifi;
using LifeAlertPlus.Shared.DTOs.Responses.Wifi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class WifiController : BaseApiController
    {
        private readonly IWifiNetworkService _wifiService;
        private readonly IUserMonitoredService _userMonitoredService;

        public WifiController(IWifiNetworkService wifiService, IUserMonitoredService userMonitoredService)
        {
            _wifiService = wifiService;
            _userMonitoredService = userMonitoredService;
        }

        private async Task<bool> UserOwnsMonitoredAsync(Guid monitoredId)
        {
            if (IsAdminRole()) return true;
            var callerId = GetCallerId();
            if (callerId == null) return false;
            return await _userMonitoredService.UserOwnsMonitoredAsync(callerId.Value, monitoredId);
        }

        [HttpGet("monitored/{idMonitored:guid}")]
        public async Task<IActionResult> GetByMonitored(Guid idMonitored)
        {
            if (!await UserOwnsMonitoredAsync(idMonitored))
                return Forbid();

            var networks = await _wifiService.GetByMonitoredIdAsync(idMonitored);
            var dto = networks.Select(w => new WifiNetworkResponseDTO
            {
                Id = w.Id,
                Ssid = w.Ssid,
                CreatedAt = w.CreatedAt
            }).ToList();
            return Ok(dto);
        }

        [HttpPost]
        public async Task<IActionResult> Add([FromBody] WifiNetworkRequestDTO request)
        {
            if (request == null || request.IdMonitored == Guid.Empty)
                return BadRequest(new { Message = "IdMonitored is required." });

            if (!await UserOwnsMonitoredAsync(request.IdMonitored))
                return Forbid();

            var (success, errorKey, network) = await _wifiService.AddAsync(request.IdMonitored, request.Ssid, request.Password);
            if (!success)
                return BadRequest(new { Error = errorKey });

            return Ok(new WifiNetworkResponseDTO
            {
                Id = network!.Id,
                Ssid = network.Ssid,
                CreatedAt = network.CreatedAt
            });
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var network = await _wifiService.GetByIdAsync(id);
            if (network == null) return NotFound();

            if (!await UserOwnsMonitoredAsync(network.IdMonitored))
                return Forbid();

            var deleted = await _wifiService.DeleteAsync(id);
            return deleted ? Ok() : NotFound();
        }
    }
}
