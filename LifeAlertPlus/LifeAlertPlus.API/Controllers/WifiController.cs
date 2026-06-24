using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Shared.DTOs.Requests.Wifi;
using LifeAlertPlus.Shared.DTOs.Responses.Wifi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LifeAlertPlus.API.Controllers
{
    // Controller pentru gestionarea rețelelor WiFi salvate pentru dispozitivele ESP32.
    // ESP32 primește lista de rețele WiFi prin endpoint-ul GetWifiConfig din ESPController
    // și încearcă să se conecteze la una dintre ele la pornire.
    // NOTĂ: parolele sunt stocate ca text simplu în DB (nu există criptare configurată) — vezi WifiNetworkRepository.
    [ApiController]
    [Authorize] // Necesită autentificare
    [Route("api/[controller]")]
    public class WifiController : BaseApiController
    {
        private readonly IWifiNetworkService _wifiService;          // Logica de business pentru rețele WiFi
        private readonly IUserMonitoredService _userMonitoredService; // Verificare drept de acces

        public WifiController(IWifiNetworkService wifiService, IUserMonitoredService userMonitoredService)
        {
            _wifiService = wifiService;
            _userMonitoredService = userMonitoredService;
        }

        // Verifică că utilizatorul curent are dreptul să gestioneze rețelele WiFi ale persoanei monitorizate
        private async Task<bool> UserOwnsMonitoredAsync(Guid monitoredId)
        {
            if (IsAdminRole()) return true; // Adminul are acces la toate
            var callerId = GetCallerId();
            if (callerId == null) return false;
            return await _userMonitoredService.UserOwnsMonitoredAsync(callerId.Value, monitoredId);
        }

        // GET /api/wifi/monitored/{idMonitored} — Lista rețelelor WiFi configurate pentru un dispozitiv
        // Returnează SSID și data adăugării, dar NU parola (securitate)
        [HttpGet("monitored/{idMonitored:guid}")]
        public async Task<IActionResult> GetByMonitored(Guid idMonitored)
        {
            if (!await UserOwnsMonitoredAsync(idMonitored))
                return Forbid();

            var networks = await _wifiService.GetByMonitoredIdAsync(idMonitored);
            // Construim DTO-ul fără parolă — clientul nu trebuie să vadă parolele stocate
            var dto = networks.Select(w => new WifiNetworkResponseDTO
            {
                Id        = w.Id,
                Ssid      = w.Ssid,      // Numele rețelei WiFi (SSID)
                CreatedAt = w.CreatedAt  // Data la care a fost adăugată rețeaua
            }).ToList();
            return Ok(dto);
        }

        // POST /api/wifi — Adaugă o nouă rețea WiFi pentru dispozitivul ESP32
        // ESP32-ul va putea să se conecteze la această rețea la pornire
        [HttpPost]
        public async Task<IActionResult> Add([FromBody] WifiNetworkRequestDTO request)
        {
            if (request == null || request.IdMonitored == Guid.Empty)
                return BadRequest(new { Message = "IdMonitored is required." });

            if (!await UserOwnsMonitoredAsync(request.IdMonitored))
                return Forbid();

            // Serviciul gestionează intern validările (ex. duplicate SSID, limită rețele)
            var (success, errorKey, network) = await _wifiService.AddAsync(request.IdMonitored, request.Ssid, request.Password);
            if (!success)
                return BadRequest(new { Error = errorKey }); // Ex: "DUPLICATE_SSID", "MAX_NETWORKS_REACHED"

            // Returnăm rețeaua salvată (fără parolă)
            return Ok(new WifiNetworkResponseDTO
            {
                Id        = network!.Id,
                Ssid      = network.Ssid,
                CreatedAt = network.CreatedAt
            });
        }

        // DELETE /api/wifi/{id} — Șterge o rețea WiFi din configurația dispozitivului
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            // Mai întâi găsim rețeaua pentru a verifica că aparține persoanei monitorizate de utilizator
            var network = await _wifiService.GetByIdAsync(id);
            if (network == null) return NotFound();

            // Verificăm că utilizatorul curent are dreptul să șteargă această rețea
            if (!await UserOwnsMonitoredAsync(network.IdMonitored))
                return Forbid();

            var deleted = await _wifiService.DeleteAsync(id);
            return deleted ? Ok() : NotFound(); // 200 OK dacă s-a șters, 404 dacă nu a existat
        }
    }
}
