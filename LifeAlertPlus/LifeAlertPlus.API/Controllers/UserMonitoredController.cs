using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Shared.DTOs.Responses.UserMonitored;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Collections.Generic;
using System.Linq;
using System;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class UserMonitoredController : ControllerBase
    {
        private readonly IUserMonitoredService _userMonitoredService;
        private readonly IRoleService _roleService;

        public UserMonitoredController(IUserMonitoredService userMonitoredService, IRoleService roleService)
        {
            _userMonitoredService = userMonitoredService;
            _roleService = roleService;
        }

        private bool CallerOwns(Guid userId)
        {
            var callerIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return callerIdStr != null && Guid.TryParse(callerIdStr, out var callerGuid) && callerGuid == userId;
        }

        [HttpGet("{userId}/monitored")]
        public async Task<IActionResult> GetMonitoredPeopleByUserId(Guid userId)
        {
            var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value ?? User.FindFirst("role")?.Value ?? string.Empty;
            if (!CallerOwns(userId) && !IsAdminRole(roleClaim))
                return Forbid();

            var monitoredPeople = await _userMonitoredService.GetMonitoredPeopleByUserIdAsync(userId);
            return Ok(monitoredPeople);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("{userId}/monitored/{monitoredPersonId}")]
        public async Task<IActionResult> AddMonitoredPersonToUser(Guid userId, Guid monitoredPersonId)
        {
            var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value ?? User.FindFirst("role")?.Value ?? string.Empty;
            if (!CallerOwns(userId) && !IsAdminRole(roleClaim))
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

        private static bool IsAdminRole(string? roleName)
        {
            if (string.IsNullOrWhiteSpace(roleName))
                return false;

            return roleName.Contains("admin", StringComparison.OrdinalIgnoreCase);
        }
    }
}