using Microsoft.AspNetCore.Mvc;
using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Shared.DTOs.Requests.User;
using LifeAlertPlus.Shared.DTOs.Responses.User;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class UserController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IMeasurementService _measurementService;
        private readonly IUserMonitoredService _userMonitoredService;
        private readonly ILogger<UserController> _logger;

        public UserController(IUserService userService, IMeasurementService measurementService, IUserMonitoredService userMonitoredService, ILogger<UserController> logger)
        {
            _userService = userService;
            _measurementService = measurementService;
            _userMonitoredService = userMonitoredService;
            _logger = logger;
        }

        private bool CallerOwns(Guid id)
        {
            var callerIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value
                ?? User.FindFirst("nameid")?.Value;
            return callerIdStr != null && Guid.TryParse(callerIdStr, out var callerGuid) && callerGuid == id;
        }

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
            user.FirstDayOfTheWeek = updatedUser.FirstDayOfTheWeek ?? user.FirstDayOfTheWeek;
            user.Language = updatedUser.Language ?? user.Language;
            user.FontSize = updatedUser.FontSize ?? user.FontSize;
            user.MinHeartRate = updatedUser.MinHeartRate ?? user.MinHeartRate;
            user.MaxHeartRate = updatedUser.MaxHeartRate ?? user.MaxHeartRate;
            user.MinTemperature = updatedUser.MinTemperature ?? user.MinTemperature;
            user.MaxTemperature = updatedUser.MaxTemperature ?? user.MaxTemperature;
            user.MinSpO2 = updatedUser.MinSpO2 ?? user.MinSpO2;
            user.MaxSpO2 = updatedUser.MaxSpO2 ?? user.MaxSpO2;
            user.UpdateFrequency = updatedUser.UpdateFrequency ?? user.UpdateFrequency;
            user.DataRetentionDays = updatedUser.DataRetentionDays ?? user.DataRetentionDays;
            user.NotifyByEmail = updatedUser.NotifyByEmail ?? user.NotifyByEmail;
            user.NotifyByPush = updatedUser.NotifyByPush ?? user.NotifyByPush;
            user.EnableDailyReport = updatedUser.EnableDailyReport ?? user.EnableDailyReport;
            user.UpdatedAt = DateTime.UtcNow;

            var result = await _userService.UpdateUserAsync(user);
            if (!result)
            {
                _logger.LogError("[UpdateUser] Failed to update user {Id} in DB", id);
                return StatusCode(500, new { Message = "Failed to update user." });
            }

            // Apply data retention cleanup if the user set a retention period
            if (user.DataRetentionDays.HasValue && user.DataRetentionDays.Value > 0)
            {
                try
                {
                    var cutoff = DateTime.UtcNow.AddDays(-user.DataRetentionDays.Value);
                    var monitoredPeople = await _userMonitoredService.GetMonitoredPeopleByUserIdAsync(id);
                    var monitoredIds = monitoredPeople.Select(m => m.Id);
                    var deleted = await _measurementService.DeleteMeasurementsOlderThanAsync(monitoredIds, cutoff);
                    if (deleted > 0)
                        _logger.LogInformation("[UpdateUser] Cleaned up {Count} old measurements for user {Id}", deleted, id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[UpdateUser] Data retention cleanup failed for user {Id}", id);
                }
            }

            _logger.LogInformation("[UpdateUser] User {Id} updated successfully", id);
            return Ok(new { Message = "User updated successfully." });
        }

        [HttpPatch("deactivate/{id}")]
        public async Task<IActionResult> DeactivateUser(Guid id)
        {
            if (!CallerOwns(id))
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
            if (!CallerOwns(id))
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

        [HttpDelete("delete/{id}")]
        public async Task<IActionResult> DeleteUser(Guid id)
        {
            if (!CallerOwns(id))
                return Forbid();

            var user = await _userService.GetUserByIdAsync(id);
            if (user == null)
            {
                return NotFound(new { Message = "User not found." });
            }

            TryDeleteProfileImageFile(user.ProfilePictureUrl);

            var result = await _userService.DeleteUserAsync(id);
            
            if (!result)
            {
                return StatusCode(500, new { Message = "Failed to delete user." });
            }

            return Ok(new { Message = "User deleted successfully." });
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
            var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value
                ?? User.FindFirst("role")?.Value
                ?? string.Empty;

            if (!CallerOwns(id) && !IsAdminRole(roleClaim))
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
                FontSize = user.FontSize ?? "medium",
                MinHeartRate = user.MinHeartRate ?? 0,
                MaxHeartRate = user.MaxHeartRate ?? 0,
                MinTemperature = (float)(user.MinTemperature ?? 0),
                MaxTemperature = (float)(user.MaxTemperature ?? 0),
                MinSpO2 = user.MinSpO2 ?? 0,
                MaxSpO2 = user.MaxSpO2 ?? 0,
                UpdateFrequency = user.UpdateFrequency ?? 30,
                DataRetentionDays = user.DataRetentionDays ?? 0,
                NotifyByEmail = user.NotifyByEmail,
                NotifyByPush = user.NotifyByPush,
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
                FontSize = user.FontSize ?? "medium",
                MinHeartRate = user.MinHeartRate ?? 0,
                MaxHeartRate = user.MaxHeartRate ?? 0,
                MinTemperature = (float)(user.MinTemperature ?? 0),
                MaxTemperature = (float)(user.MaxTemperature ?? 0),
                MinSpO2 = user.MinSpO2 ?? 0,
                MaxSpO2 = user.MaxSpO2 ?? 0,
                UpdateFrequency = user.UpdateFrequency ?? 30,
                DataRetentionDays = user.DataRetentionDays ?? 0,
                NotifyByEmail = user.NotifyByEmail,
                NotifyByPush = user.NotifyByPush,
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

        private static bool IsAdminRole(string? role)
        {
            return !string.IsNullOrWhiteSpace(role)
                && role.IndexOf("Admin", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}