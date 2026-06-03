using Microsoft.AspNetCore.Mvc;
using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Shared.DTOs.Requests.User;
using LifeAlertPlus.Shared.DTOs.Responses.User;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using LifeAlertPlus.Infrastructure.Context;
using LifeAlertPlus.API.Services;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class UserController : BaseApiController
    {
        private readonly IUserService _userService;
        private readonly ILogger<UserController> _logger;
        private readonly LifeAlertPlusDbContext _db;
        private readonly AuditService _auditService;

        public UserController(IUserService userService, ILogger<UserController> logger, LifeAlertPlusDbContext db, AuditService auditService)
        {
            _userService = userService;
            _logger = logger;
            _db = db;
            _auditService = auditService;
        }

        private bool CallerOwns(Guid id) => GetCallerId() == id;

        private bool CallerOwn(string email)
        {
            var callerEmail = User.FindFirst(ClaimTypes.Email)?.Value
                ?? User.FindFirst("email")?.Value;
            return callerEmail != null && callerEmail.Equals(email, StringComparison.OrdinalIgnoreCase);
        }

        private static readonly HashSet<string> _allowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
            { ".jpg", ".jpeg", ".png", ".gif", ".webp" };

        private static readonly HashSet<string> _allowedImageMimeTypes = new(StringComparer.OrdinalIgnoreCase)
            { "image/jpeg", "image/png", "image/gif", "image/webp" };

        [HttpPut("update/{id}")]
        public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UserUpdateRequestDTO updatedUser)
        {
            if (!CallerOwns(id))
                return Forbid();

            var user = await _userService.GetUserByIdAsync(id);
            _logger.LogInformation("[UpdateUser] Received: FirstName={FirstName}, LastName={LastName}, FirstDayOfTheWeek={FirstDayOfTheWeek}", updatedUser.FirstName, updatedUser.LastName, updatedUser.FirstDayOfTheWeek);
            if (user == null)
            {
                _logger.LogWarning("[UpdateUser] User not found for id {Id}", id);
                return NotFound(new { Message = "User not found." });
            }

            user.FirstName = updatedUser.FirstName ?? user.FirstName;
            user.LastName = updatedUser.LastName ?? user.LastName;
            if (updatedUser.PhoneNumber != null)
                user.PhoneNumber = string.IsNullOrWhiteSpace(updatedUser.PhoneNumber) ? null : updatedUser.PhoneNumber.Trim();
            if (!string.IsNullOrEmpty(updatedUser.FirstDayOfTheWeek)) user.FirstDayOfTheWeek = updatedUser.FirstDayOfTheWeek;
            if (!string.IsNullOrEmpty(updatedUser.Language)) user.Language = updatedUser.Language;
            user.MinHeartRate = updatedUser.MinHeartRate ?? user.MinHeartRate;
            user.MaxHeartRate = updatedUser.MaxHeartRate ?? user.MaxHeartRate;
            user.MinTemperature = updatedUser.MinTemperature ?? user.MinTemperature;
            user.MaxTemperature = updatedUser.MaxTemperature ?? user.MaxTemperature;
            user.MinSpO2 = updatedUser.MinSpO2 ?? user.MinSpO2;
            user.MaxSpO2 = updatedUser.MaxSpO2 ?? user.MaxSpO2;
            user.UpdateFrequency = updatedUser.UpdateFrequency ?? user.UpdateFrequency;
            user.NotifyByEmail = updatedUser.NotifyByEmail ?? user.NotifyByEmail;
            user.NotifyByPush = updatedUser.NotifyByPush ?? user.NotifyByPush;
            user.NotifyBySms = updatedUser.NotifyBySms ?? user.NotifyBySms;
            user.EnableDailyReport = updatedUser.EnableDailyReport ?? user.EnableDailyReport;
            user.UpdatedAt = DateTime.UtcNow;

            var result = await _userService.UpdateUserAsync(user);
            if (!result)
            {
                _logger.LogError("[UpdateUser] Failed to update user {Id} in DB", id);
                return StatusCode(500, new { Message = "Failed to update user." });
            }

            _logger.LogInformation("[UpdateUser] User {Id} updated successfully", id);
            return Ok(new { Message = "User updated successfully." });
        }

        [HttpPatch("deactivate/{id}")]
        public async Task<IActionResult> DeactivateUser(Guid id)
        {
            if (!CallerOwns(id) && !IsAdminRole())
                return Forbid();

            var user = await _userService.GetUserByIdAsync(id);
            if (user == null)
            {
                return NotFound(new { Message = "User not found." });
            }

            user.UpdatedAt = DateTime.UtcNow;
            user.DeletedAt = DateTime.UtcNow;

            var result = await _userService.UpdateUserAsync(user);
            if (!result)
            {
                return StatusCode(500, new { Message = "Failed to deactivate user." });
            }

            return Ok(new { Message = "User deactivated successfully." });
        }

        [HttpPatch("activate/{id}")]
        public async Task<IActionResult> ActivateUser(Guid id)
        {
            if (!CallerOwns(id) && !IsAdminRole())
                return Forbid();

            var user = await _userService.GetUserByIdAsync(id);
            if (user == null)
            {
                return NotFound(new { Message = "User not found." });
            }

            user.UpdatedAt = DateTime.UtcNow;
            user.DeletedAt = null;

            var result = await _userService.UpdateUserAsync(user);
            if (!result)
            {
                return StatusCode(500, new { Message = "Failed to activate user." });
            }

            return Ok(new { Message = "User activated successfully." });
        }


        private static void TryDeleteProfileImageFile(string? profilePictureUrl)
        {
            if (string.IsNullOrWhiteSpace(profilePictureUrl))
                return;

            if (!Uri.TryCreate(profilePictureUrl, UriKind.Absolute, out var pictureUri))
                return;

            if (!pictureUri.AbsolutePath.StartsWith("/profile-images/", StringComparison.OrdinalIgnoreCase))
                return;

            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "profile-images");
            var fileName = Path.GetFileName(pictureUri.LocalPath);
            if (string.IsNullOrWhiteSpace(fileName))
                return;

            var filePath = Path.Combine(uploadsFolder, fileName);
            if (System.IO.File.Exists(filePath))
            {
                System.IO.File.Delete(filePath);
            }
        }

        [HttpPost("upload-profile-image/{id}")]
        public async Task<IActionResult> UploadProfileImage(Guid id, IFormFile file)
        {
            if (!CallerOwns(id))
                return Forbid();

            if (file == null || file.Length == 0)
                return BadRequest(new { Message = "No file uploaded." });

            var extension = Path.GetExtension(file.FileName);
            if (!_allowedImageExtensions.Contains(extension))
                return BadRequest(new { Message = "Invalid file type. Only JPG, PNG, GIF and WebP images are allowed." });

            if (!_allowedImageMimeTypes.Contains(file.ContentType))
                return BadRequest(new { Message = "Invalid file type. Only image files are allowed." });

            if (file.Length > 5 * 1024 * 1024)
                return BadRequest(new { Message = "File size exceeds the 5 MB limit." });

            var user = await _userService.GetUserByIdAsync(id);
            if (user == null)
                return NotFound(new { Message = "User not found." });

            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "profile-images");
            if (!Directory.Exists(uploadsFolder))
                Directory.CreateDirectory(uploadsFolder);

            if (!string.IsNullOrWhiteSpace(user.ProfilePictureUrl) &&
                Uri.TryCreate(user.ProfilePictureUrl, UriKind.Absolute, out var previousPictureUri) &&
                previousPictureUri.AbsolutePath.StartsWith("/profile-images/", StringComparison.OrdinalIgnoreCase))
            {
                var previousFileName = Path.GetFileName(previousPictureUri.LocalPath);
                if (!string.IsNullOrWhiteSpace(previousFileName))
                {
                    var previousFilePath = Path.Combine(uploadsFolder, previousFileName);
                    if (System.IO.File.Exists(previousFilePath))
                    {
                        System.IO.File.Delete(previousFilePath);
                    }
                }
            }

            var fileName = $"{id}_{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            var filePath = Path.Combine(uploadsFolder, fileName);
            await using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var request = HttpContext.Request;
            var baseUrl = $"{request.Scheme}://{request.Host}";
            var absoluteUrl = $"{baseUrl}/profile-images/{fileName}";
            user.ProfilePictureUrl = absoluteUrl;
            user.UpdatedAt = DateTime.UtcNow;
            var result = await _userService.UpdateUserAsync(user);
            if (!result)
                return StatusCode(500, new { Message = "Failed to update user profile image." });

            return Ok(new { Message = "Profile image uploaded successfully.", ImageUrl = absoluteUrl });
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetUserById(Guid id)
        {
            if (!CallerOwns(id) && !IsAdminRole())
                return Forbid();

            var user = await _userService.GetUserByIdAsync(id);
            if (user == null)
            {
                return NotFound(new { Message = "User not found." });
            }

            return Ok(new UserProfileDTO
            {
                Id = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                ProfilePictureUrl = user.ProfilePictureUrl,
                IsEmailConfirmed = user.IsEmailConfirmed,
                Provider = string.IsNullOrEmpty(user.Provider) ? "Local" : user.Provider,
                FirstDayOfTheWeek = user.FirstDayOfTheWeek,
                Language = user.Language ?? "en",
                MinHeartRate = user.MinHeartRate ?? 0,
                MaxHeartRate = user.MaxHeartRate ?? 0,
                MinTemperature = (float)(user.MinTemperature ?? 0),
                MaxTemperature = (float)(user.MaxTemperature ?? 0),
                MinSpO2 = user.MinSpO2 ?? 0,
                MaxSpO2 = user.MaxSpO2 ?? 0,
                UpdateFrequency = user.UpdateFrequency ?? 30,
                NotifyByEmail = user.NotifyByEmail,
                NotifyByPush = user.NotifyByPush,
                NotifyBySms = user.NotifyBySms,
                EnableDailyReport = user.EnableDailyReport,
                LastChangedPasswordAt = user.LastChangedPasswordAt,
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt
            });
        }

        [HttpGet("email/{email}")]
        public async Task<IActionResult> GetUserByEmail(string email)
        {
            if(!CallerOwn(email))
                return Forbid();

            var user = await _userService.GetUserByEmailAsync(email);

            if (user == null)
            {
                return NotFound(new { Message = "User not found." });
            }

            return Ok(new UserProfileDTO
            {
                Id = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                ProfilePictureUrl = user.ProfilePictureUrl,
                IsEmailConfirmed = user.IsEmailConfirmed,
                Provider = string.IsNullOrEmpty(user.Provider) ? "Local" : user.Provider,
                FirstDayOfTheWeek = user.FirstDayOfTheWeek,
                Language = user.Language ?? "en",
                MinHeartRate = user.MinHeartRate ?? 0,
                MaxHeartRate = user.MaxHeartRate ?? 0,
                MinTemperature = (float)(user.MinTemperature ?? 0),
                MaxTemperature = (float)(user.MaxTemperature ?? 0),
                MinSpO2 = user.MinSpO2 ?? 0,
                MaxSpO2 = user.MaxSpO2 ?? 0,
                UpdateFrequency = user.UpdateFrequency ?? 30,
                NotifyByEmail = user.NotifyByEmail,
                NotifyByPush = user.NotifyByPush,
                NotifyBySms = user.NotifyBySms,
                EnableDailyReport = user.EnableDailyReport,
                LastChangedPasswordAt = user.LastChangedPasswordAt,
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt
            });
        }

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAllUsers()
        {
            var users = await _userService.GetAllUsersAsync();
            var response = new List<UserListItemDTO>();

            foreach (var user in users)
            {
                if (IsAdminRole(user.Role?.Name))
                    continue;

                response.Add(new UserListItemDTO
                {
                    Id = user.Id,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    Email = user.Email,
                    ProfilePictureUrl = user.ProfilePictureUrl,
                    IsEmailConfirmed = user.IsEmailConfirmed,
                    Provider = user.Provider ?? "Local",
                    Role = user.Role?.Name ?? "User",
                    CreatedAt = user.CreatedAt,
                    UpdatedAt = user.UpdatedAt,
                    DeletedAt = user.DeletedAt,
                    LastChangedPasswordAt = user.LastChangedPasswordAt
                });
            }

            return Ok(response);
        }

        // ── GDPR consent — records first-time acceptance ────────────────────────
        [HttpPost("{id}/consent")]
        public async Task<IActionResult> RecordConsent(Guid id)
        {
            if (!CallerOwns(id)) return Forbid();
            var user = await _userService.GetUserByIdAsync(id);
            if (user == null) return NotFound();
            if (user.DataProcessingConsentAt != null)
                return Ok(new { Message = "Consent already recorded." });
            user.DataProcessingConsentAt = DateTime.UtcNow;
            user.UpdatedAt = DateTime.UtcNow;
            await _userService.UpdateUserAsync(user);
            _logger.LogInformation("GDPR consent recorded for user {UserId} (Google first login)", id);
            // Issue a new token without the needsConsent flag.
            return Ok(new { Message = "Consent recorded." });
        }

        // ── GDPR Art. 20 — Data portability export ──────────────────────────────
        [HttpGet("{id}/gdpr-export")]
        public async Task<IActionResult> GdprExport(Guid id)
        {
            if (!CallerOwns(id)) return Forbid();

            var user = await _userService.GetUserByIdAsync(id);
            if (user == null) return NotFound();

            var monitoredIds = await _db.UserMonitoreds
                .Where(um => um.IdUser == id)
                .Select(um => um.IdMonitored)
                .ToListAsync();

            var monitoreds = await _db.Monitoreds
                .Where(m => monitoredIds.Contains(m.Id))
                .ToListAsync();

            var measurements = await _db.Measurements
                .Where(m => monitoredIds.Contains(m.IdMonitored))
                .OrderByDescending(m => m.CreatedAt)
                .ToListAsync();

            var notifications = await _db.Notifications
                .Where(n => n.IdUser == id || monitoredIds.Contains(n.IdMonitored))
                .OrderByDescending(n => n.CreatedAt)
                .ToListAsync();

            var activityProfiles = await _db.ActivityProfiles
                .Where(ap => monitoredIds.Contains(ap.IdMonitored))
                .ToListAsync();

            var export = new
            {
                ExportedAt = DateTime.UtcNow,
                GdprNote = "This file contains all personal data held by LifeAlertPlus about you and the persons you monitor, exported under GDPR Art. 20 (right to data portability).",
                Account = new
                {
                    user.Id,
                    user.FirstName,
                    user.LastName,
                    user.Email,
                    user.PhoneNumber,
                    user.Provider,
                    user.Language,
                    user.CreatedAt,
                    user.DataProcessingConsentAt
                },
                MonitoredPersons = monitoreds.Select(m => new
                {
                    m.Id,
                    m.FirstName,
                    m.LastName,
                    m.Birthdate,
                    m.Gender,
                    m.Address,
                    m.DeviceSerialNumber,
                    m.DataRetentionDays,
                    m.ArchiveRetentionDays,
                    m.IsArchived,
                    m.ArchivedAt,
                    m.CreatedAt
                }),
                Measurements = measurements.Select(m => new
                {
                    m.IdMonitored,
                    m.Pulse,
                    m.Temperature,
                    m.SpO2,
                    m.IsFall,
                    m.Activity,
                    m.Coordinates,
                    m.CreatedAt
                }),
                Notifications = notifications.Select(n => new
                {
                    n.IdMonitored,
                    n.NotificationType,
                    n.Message,
                    n.IsRead,
                    n.CreatedAt
                }),
                ActivityProfiles = activityProfiles.Select(ap => new
                {
                    ap.IdMonitored,
                    ap.HourOfDay,
                    ap.AveragePulse,
                    ap.MovementRate,
                    ap.SleepProbability,
                    ap.DataPoints,
                    ap.LastUpdated
                })
            };

            var json = System.Text.Json.JsonSerializer.Serialize(export,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            var fileName = $"lifealertplus-gdpr-export-{DateTime.UtcNow:yyyyMMdd}.json";

            _logger.LogInformation("GDPR export generated for user {UserId}", id);
            return File(bytes, "application/json", fileName);
        }

        // ── GDPR Art. 17 — Right to erasure (account + all personal data) ───────
        [HttpDelete("delete/{id}")]
        public async Task<IActionResult> DeleteUser(Guid id)
        {
            if (!CallerOwns(id) && !IsAdminRole()) return Forbid();

            var user = await _userService.GetUserByIdAsync(id);
            if (user == null) return NotFound(new { Message = "User not found." });

            TryDeleteProfileImageFile(user.ProfilePictureUrl);

            // Delete monitored persons that have no other caregiver.
            // Persons shared with other users lose only this user's link — their data is preserved.
            var ownedLinks = await _db.UserMonitoreds
                .Where(um => um.IdUser == id)
                .ToListAsync();

            foreach (var link in ownedLinks)
            {
                var otherOwners = await _db.UserMonitoreds
                    .CountAsync(um => um.IdMonitored == link.IdMonitored && um.IdUser != id);

                if (otherOwners == 0)
                {
                    // No other caregiver — hard-delete the person and all cascade data.
                    var monitored = await _db.Monitoreds.FindAsync(link.IdMonitored);
                    if (monitored != null)
                        _db.Monitoreds.Remove(monitored);
                }
                else
                {
                    // Shared — only remove the link.
                    _db.UserMonitoreds.Remove(link);
                }
            }

            // Remove notifications addressed to this user for monitored persons
            // that are NOT being fully deleted (i.e. shared persons — the monitored
            // person stays but this user's notifications for them should go).
            var remainingMonitoredIds = ownedLinks
                .Where(l => _db.UserMonitoreds.Any(um => um.IdMonitored == l.IdMonitored && um.IdUser != id))
                .Select(l => l.IdMonitored)
                .ToHashSet();
            if (remainingMonitoredIds.Count > 0)
            {
                var sharedNotifs = await _db.Notifications
                    .Where(n => n.IdUser == id && remainingMonitoredIds.Contains(n.IdMonitored))
                    .ToListAsync();
                _db.Notifications.RemoveRange(sharedNotifs);
            }

            await _db.SaveChangesAsync();

            var result = await _userService.DeleteUserAsync(id);
            if (!result) return StatusCode(500, new { Message = "Failed to delete user account." });

            var byAdmin = IsAdminRole() && !CallerOwns(id);
            var actorEmail = byAdmin
                ? (User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "admin")
                : user.Email;
            var auditDetails = byAdmin
                ? $"Account permanently deleted by administrator (deleted user: {user.Email})"
                : "User deleted their own account (GDPR Art. 17)";
            _logger.LogWarning("Account {Email} permanently deleted (by admin: {ByAdmin})", user.Email, byAdmin);
            _auditService.LogAsync(actorEmail, "DeleteAccount", auditDetails, "Account");
            return Ok(new { Message = "Account and all associated data permanently deleted." });
        }

        [HttpPatch("{id}/daily-report")]
        public async Task<IActionResult> ToggleDailyReport(Guid id, [FromBody] ToggleDailyReportRequest request)
        {
            if (!CallerOwns(id))
                return Forbid();

            var user = await _userService.GetUserByIdAsync(id);
            if (user == null)
                return NotFound();

            user.EnableDailyReport = request.Enabled;
            await _userService.UpdateUserAsync(user);

            return Ok(new { EnableDailyReport = request.Enabled });
        }

    }
}