using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Shared.DTOs.Responses.UserMonitored;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using System;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class UserMonitoredController : BaseApiController
    {
        private readonly IUserMonitoredService _userMonitoredService;
        private readonly IRoleService _roleService;

        public UserMonitoredController(IUserMonitoredService userMonitoredService, IRoleService roleService)
        {
            _userMonitoredService = userMonitoredService;
            _roleService = roleService;
        }

        [HttpGet("{userId}/monitored")]
        public async Task<IActionResult> GetMonitoredPeopleByUserId(Guid userId, [FromQuery] bool includeArchived = false)
        {
            if (GetCallerId() != userId && !IsAdminRole())
                return Forbid();

            var monitoredPeople = includeArchived
                ? await _userMonitoredService.GetMonitoredPeopleByUserIdAsync(userId)
                : await _userMonitoredService.GetActiveMonitoredPeopleByUserIdAsync(userId);
            return Ok(monitoredPeople);
        }

        [HttpGet("{userId}/monitored/archived")]
        public async Task<IActionResult> GetArchivedMonitoredPeopleByUserId(Guid userId)
        {
            if (GetCallerId() != userId && !IsAdminRole())
                return Forbid();

            var monitoredPeople = await _userMonitoredService.GetArchivedMonitoredPeopleByUserIdAsync(userId);
            return Ok(monitoredPeople);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("{userId}/monitored/{monitoredPersonId}")]
        public async Task<IActionResult> AddMonitoredPersonToUser(Guid userId, Guid monitoredPersonId)
        {
            if (GetCallerId() != userId && !IsAdminRole())
                return Forbid();

            await _userMonitoredService.AddMonitoredPersonToUserAsync(userId, monitoredPersonId);
            return NoContent();
        }

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAllMonitoredUsers()
        {
            var userMonitored = await _userMonitoredService.GetAllUserMonitoredWithDetailsAsync();

            var nonAdmin = userMonitored
                .Where(um => !IsAdminRole(um.User.Role?.Name))
                .ToList();
            var grouped = nonAdmin.GroupBy(um => um.IdUser);
            var response = new List<MonitoredUserDTO>();

            foreach (var group in grouped)
            {
                var user = group.First().User;

                var monitoredPeople = group
                    .Select(g => g.Monitored)
                    .Select(m => new MonitoredPersonDTO
                    {
                        Id = m.Id,
                        FirstName = m.FirstName,
                        LastName = m.LastName,
                        DeviceSerialNumber = m.DeviceSerialNumber,
                        IsActive = m.IsActive,
                        IsArchived = m.IsArchived,
                        ArchivedAt = m.ArchivedAt,
                        CreatedAt = m.CreatedAt,
                        UpdatedAt = m.UpdatedAt,
                        DeletedAt = m.DeletedAt
                    })
                    .ToList();

                response.Add(new MonitoredUserDTO
                {
                    UserId = user.Id,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    Email = user.Email,
                    Role = user.Role?.Name ?? "User",
                    IsActive = user.DeletedAt == null,
                    Provider = user.Provider ?? "Local",
                    CreatedAt = user.CreatedAt,
                    UpdatedAt = user.UpdatedAt,
                    MonitoredPeople = monitoredPeople
                });
            }

            return Ok(response);
        }

    }
}