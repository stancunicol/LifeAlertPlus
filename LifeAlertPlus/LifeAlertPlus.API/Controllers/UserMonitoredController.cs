using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Shared.DTOs.Responses.UserMonitored;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using System;

namespace LifeAlertPlus.API.Controllers
{
    // Controller pentru relația many-to-many dintre utilizatori și persoanele monitorizate.
    // Tabelul UserMonitoreds leagă fiecare utilizator de persoanele pe care le îngrijește:
    // un utilizator poate monitoriza mai multe persoane, iar o persoană poate fi urmărită de mai mulți utilizatori.
    [ApiController]
    [Authorize] // Necesită autentificare
    [Route("api/[controller]")]
    public class UserMonitoredController : BaseApiController
    {
        private readonly IUserMonitoredService _userMonitoredService; // Logica de business pentru relația user-monitored
        private readonly IRoleService _roleService;                    // Verificare rol (admin vs utilizator normal)

        public UserMonitoredController(IUserMonitoredService userMonitoredService, IRoleService roleService)
        {
            _userMonitoredService = userMonitoredService;
            _roleService = roleService;
        }

        // GET /api/usermonitored/{userId}/monitored?includeArchived=false — Lista persoanelor monitorizate de un utilizator
        // includeArchived=false (default): returnează doar persoanele active (nearhivate)
        // includeArchived=true: include și persoanele arhivate (vizibile în secțiunea Arhivă)
        [HttpGet("{userId}/monitored")]
        public async Task<IActionResult> GetMonitoredPeopleByUserId(Guid userId, [FromQuery] bool includeArchived = false)
        {
            // Securitate: utilizatorul poate vedea doar propriile persoane monitorizate; adminul vede pe ale oricui
            if (GetCallerId() != userId && !IsAdminRole())
                return Forbid();

            var monitoredPeople = includeArchived
                ? await _userMonitoredService.GetMonitoredPeopleByUserIdAsync(userId)           // Toate (inclusiv arhivate)
                : await _userMonitoredService.GetActiveMonitoredPeopleByUserIdAsync(userId);    // Doar active
            return Ok(monitoredPeople);
        }

        // GET /api/usermonitored/{userId}/monitored/archived — Lista persoanelor ARHIVATE de un utilizator
        // Arhivarea = persoana nu mai e monitorizată activ, dar datele ei sunt păstrate
        [HttpGet("{userId}/monitored/archived")]
        public async Task<IActionResult> GetArchivedMonitoredPeopleByUserId(Guid userId)
        {
            if (GetCallerId() != userId && !IsAdminRole())
                return Forbid();

            var monitoredPeople = await _userMonitoredService.GetArchivedMonitoredPeopleByUserIdAsync(userId);
            return Ok(monitoredPeople);
        }

        // POST /api/usermonitored/{userId}/monitored/{monitoredPersonId} — Linkuiește manual o persoană la un utilizator
        // Disponibil numai adminilor — utilizatorii normali adaugă persoane prin MonitoredController/add
        [Authorize(Roles = "Admin")]
        [HttpPost("{userId}/monitored/{monitoredPersonId}")]
        public async Task<IActionResult> AddMonitoredPersonToUser(Guid userId, Guid monitoredPersonId)
        {
            if (GetCallerId() != userId && !IsAdminRole())
                return Forbid();

            await _userMonitoredService.AddMonitoredPersonToUserAsync(userId, monitoredPersonId);
            return NoContent(); // 204 — operație reușită fără corp de răspuns
        }

        // GET /api/usermonitored — Lista TUTUROR utilizatorilor cu persoanele lor monitorizate
        // Endpoint exclusiv pentru panoul de admin — afișează overview-ul complet al sistemului
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAllMonitoredUsers()
        {
            var userMonitored = await _userMonitoredService.GetAllUserMonitoredWithDetailsAsync();

            // Excludem utilizatorii admini din raport (nu monitorizează pacienți)
            var nonAdmin = userMonitored
                .Where(um => !IsAdminRole(um.User.Role?.Name))
                .ToList();

            // Grupăm legăturile UserMonitored pe utilizator (un utilizator poate apărea în mai multe rânduri)
            var grouped = nonAdmin.GroupBy(um => um.IdUser);
            var response = new List<MonitoredUserDTO>();

            foreach (var group in grouped)
            {
                var user = group.First().User; // Informațiile utilizatorului (aceleași indiferent câte persoane monitorizează)

                // Construim lista persoanelor monitorizate de acest utilizator
                var monitoredPeople = group
                    .Select(g => g.Monitored)
                    .Select(m => new MonitoredPersonDTO
                    {
                        Id                 = m.Id,
                        FirstName          = m.FirstName,
                        LastName           = m.LastName,
                        DeviceSerialNumber = m.DeviceSerialNumber,
                        IsActive           = m.IsActive,
                        IsArchived         = m.IsArchived,
                        ArchivedAt         = m.ArchivedAt,
                        CreatedAt          = m.CreatedAt,
                        UpdatedAt          = m.UpdatedAt,
                        DeletedAt          = m.DeletedAt // null = activ; non-null = marcat pentru ștergere
                    })
                    .ToList();

                response.Add(new MonitoredUserDTO
                {
                    UserId          = user.Id,
                    FirstName       = user.FirstName,
                    LastName        = user.LastName,
                    Email           = user.Email,
                    Role            = user.Role?.Name ?? "User",
                    IsActive        = user.DeletedAt == null, // Contul e activ dacă DeletedAt e null
                    Provider        = user.Provider ?? "Local", // "Local" sau "Google"
                    CreatedAt       = user.CreatedAt,
                    UpdatedAt       = user.UpdatedAt,
                    MonitoredPeople = monitoredPeople
                });
            }

            return Ok(response);
        }
    }
}