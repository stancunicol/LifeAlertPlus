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
    // Controller pentru gestionarea contului de utilizator.
    // Acoperă: actualizare profil, activare/dezactivare cont, upload fotografie profil,
    // citire profil, conformitate GDPR (export date Art. 20, ștergere cont Art. 17).
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class UserController : BaseApiController
    {
        private readonly IUserService _userService;           // Logica de business pentru utilizatori
        private readonly ILogger<UserController> _logger;
        private readonly LifeAlertPlusDbContext _db;          // Acces direct la DB (EF Core)
        private readonly AuditService _auditService;          // Logarea acțiunilor importante

        public UserController(IUserService userService, ILogger<UserController> logger, LifeAlertPlusDbContext db, AuditService auditService)
        {
            _userService  = userService;
            _logger       = logger;
            _db           = db;
            _auditService = auditService;
        }

        // Verifică că utilizatorul curent este proprietarul resursei după ID
        private bool CallerOwns(Guid id) => GetCallerId() == id;

        // Verifică că utilizatorul curent este proprietarul resursei după email
        private bool CallerOwn(string email)
        {
            var callerEmail = User.FindFirst(ClaimTypes.Email)?.Value
                ?? User.FindFirst("email")?.Value;
            return callerEmail != null && callerEmail.Equals(email, StringComparison.OrdinalIgnoreCase);
        }

        // Liste albe de extensii și MIME types acceptate pentru imaginile de profil
        private static readonly HashSet<string> _allowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
            { ".jpg", ".jpeg", ".png", ".gif", ".webp" };

        private static readonly HashSet<string> _allowedImageMimeTypes = new(StringComparer.OrdinalIgnoreCase)
            { "image/jpeg", "image/png", "image/gif", "image/webp" };

        // PUT /api/user/update/{id} — Actualizează profilul utilizatorului
        // Actualizează parțial: câmpurile null în DTO sunt ignorate (nu suprascriu valorile existente)
        [HttpPut("update/{id}")]
        public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UserUpdateRequestDTO updatedUser)
        {
            if (!CallerOwns(id))
                return Forbid(); // Utilizatorul poate actualiza doar propriul cont

            var user = await _userService.GetUserByIdAsync(id);
            _logger.LogInformation("[UpdateUser] Received: FirstName={FirstName}, LastName={LastName}, FirstDayOfTheWeek={FirstDayOfTheWeek}", updatedUser.FirstName, updatedUser.LastName, updatedUser.FirstDayOfTheWeek);
            if (user == null)
            {
                _logger.LogWarning("[UpdateUser] User not found for id {Id}", id);
                return NotFound(new { Message = "User not found." });
            }

            // Actualizăm câmpurile — dacă e null în DTO, păstrăm valoarea existentă din DB
            user.FirstName = updatedUser.FirstName ?? user.FirstName;
            user.LastName  = updatedUser.LastName  ?? user.LastName;
            // Dacă PhoneNumber este trimis dar gol, îl ștergem (null); dacă nu e trimis (null), îl păstrăm
            if (updatedUser.PhoneNumber != null)
                user.PhoneNumber = string.IsNullOrWhiteSpace(updatedUser.PhoneNumber) ? null : updatedUser.PhoneNumber.Trim();
            if (!string.IsNullOrEmpty(updatedUser.FirstDayOfTheWeek)) user.FirstDayOfTheWeek = updatedUser.FirstDayOfTheWeek;
            if (!string.IsNullOrEmpty(updatedUser.Language))          user.Language           = updatedUser.Language;
            // Praguri implicite de sănătate ale utilizatorului (folosite ca default pentru persoanele monitorizate)
            user.MinHeartRate   = updatedUser.MinHeartRate   ?? user.MinHeartRate;
            user.MaxHeartRate   = updatedUser.MaxHeartRate   ?? user.MaxHeartRate;
            user.MinTemperature = updatedUser.MinTemperature ?? user.MinTemperature;
            user.MaxTemperature = updatedUser.MaxTemperature ?? user.MaxTemperature;
            user.MinSpO2        = updatedUser.MinSpO2        ?? user.MinSpO2;
            user.MaxSpO2        = updatedUser.MaxSpO2        ?? user.MaxSpO2;
            user.UpdateFrequency    = updatedUser.UpdateFrequency    ?? user.UpdateFrequency;
            user.NotifyByEmail      = updatedUser.NotifyByEmail      ?? user.NotifyByEmail;
            user.NotifyByPush       = updatedUser.NotifyByPush       ?? user.NotifyByPush;
            user.NotifyBySms        = updatedUser.NotifyBySms        ?? user.NotifyBySms;
            user.EnableDailyReport  = updatedUser.EnableDailyReport  ?? user.EnableDailyReport;
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

        // PATCH /api/user/deactivate/{id} — Dezactivează contul (soft-delete: setează DeletedAt)
        // Contul dezactivat nu mai poate fi accesat, dar datele sunt păstrate în DB
        [HttpPatch("deactivate/{id}")]
        public async Task<IActionResult> DeactivateUser(Guid id)
        {
            if (!CallerOwns(id) && !IsAdminRole())
                return Forbid();

            var user = await _userService.GetUserByIdAsync(id);
            if (user == null)
                return NotFound(new { Message = "User not found." });

            // Soft-delete: marcăm ca șters fără a elimina fizic din DB
            user.UpdatedAt = DateTime.UtcNow;
            user.DeletedAt = DateTime.UtcNow;

            var result = await _userService.UpdateUserAsync(user);
            if (!result)
                return StatusCode(500, new { Message = "Failed to deactivate user." });

            return Ok(new { Message = "User deactivated successfully." });
        }

        // PATCH /api/user/activate/{id} — Reactivează un cont dezactivat (anulează soft-delete)
        [HttpPatch("activate/{id}")]
        public async Task<IActionResult> ActivateUser(Guid id)
        {
            if (!CallerOwns(id) && !IsAdminRole())
                return Forbid();

            var user = await _userService.GetUserByIdAsync(id);
            if (user == null)
                return NotFound(new { Message = "User not found." });

            user.UpdatedAt = DateTime.UtcNow;
            user.DeletedAt = null; // Setăm null → contul e activ din nou

            var result = await _userService.UpdateUserAsync(user);
            if (!result)
                return StatusCode(500, new { Message = "Failed to activate user." });

            return Ok(new { Message = "User activated successfully." });
        }

        // Helper: șterge fișierul de imagine al profilului de pe disk (dacă e stocat local)
        // Verifică că URL-ul este local (/profile-images/) pentru a nu șterge imagini externe (Google)
        private static void TryDeleteProfileImageFile(string? profilePictureUrl)
        {
            if (string.IsNullOrWhiteSpace(profilePictureUrl))
                return;

            if (!Uri.TryCreate(profilePictureUrl, UriKind.Absolute, out var pictureUri))
                return;

            // Ștergem doar dacă e imagine stocată local (nu avatar Google)
            if (!pictureUri.AbsolutePath.StartsWith("/profile-images/", StringComparison.OrdinalIgnoreCase))
                return;

            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "profile-images");
            var fileName = Path.GetFileName(pictureUri.LocalPath);
            if (string.IsNullOrWhiteSpace(fileName))
                return;

            var filePath = Path.Combine(uploadsFolder, fileName);
            if (System.IO.File.Exists(filePath))
                System.IO.File.Delete(filePath);
        }

        // POST /api/user/upload-profile-image/{id} — Încarcă o nouă fotografie de profil
        // Validare dublă: extensie de fișier ȘI MIME type (previne upload de fișiere cu extensie falsificată)
        // Limitat la 5 MB; imaginea veche este ștearsă de pe disk la înlocuire
        [HttpPost("upload-profile-image/{id}")]
        public async Task<IActionResult> UploadProfileImage(Guid id, IFormFile file)
        {
            if (!CallerOwns(id))
                return Forbid();

            if (file == null || file.Length == 0)
                return BadRequest(new { Message = "No file uploaded." });

            // Verificăm extensia din numele fișierului
            var extension = Path.GetExtension(file.FileName);
            if (!_allowedImageExtensions.Contains(extension))
                return BadRequest(new { Message = "Invalid file type. Only JPG, PNG, GIF and WebP images are allowed." });

            // Verificăm și MIME type-ul declarat de browser (a doua linie de apărare)
            if (!_allowedImageMimeTypes.Contains(file.ContentType))
                return BadRequest(new { Message = "Invalid file type. Only image files are allowed." });

            if (file.Length > 5 * 1024 * 1024) // Limita de 5 MB
                return BadRequest(new { Message = "File size exceeds the 5 MB limit." });

            var user = await _userService.GetUserByIdAsync(id);
            if (user == null)
                return NotFound(new { Message = "User not found." });

            // Creăm folderul dacă nu există
            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "profile-images");
            if (!Directory.Exists(uploadsFolder))
                Directory.CreateDirectory(uploadsFolder);

            // Ștergem imaginea veche de pe disk (dacă e locală, nu externă)
            if (!string.IsNullOrWhiteSpace(user.ProfilePictureUrl) &&
                Uri.TryCreate(user.ProfilePictureUrl, UriKind.Absolute, out var previousPictureUri) &&
                previousPictureUri.AbsolutePath.StartsWith("/profile-images/", StringComparison.OrdinalIgnoreCase))
            {
                var previousFileName = Path.GetFileName(previousPictureUri.LocalPath);
                if (!string.IsNullOrWhiteSpace(previousFileName))
                {
                    var previousFilePath = Path.Combine(uploadsFolder, previousFileName);
                    if (System.IO.File.Exists(previousFilePath))
                        System.IO.File.Delete(previousFilePath);
                }
            }

            // Generăm un nume unic: {userId}_{guid}.{extensie} — previne conflicte și ghicire
            var fileName = $"{id}_{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            var filePath = Path.Combine(uploadsFolder, fileName);
            await using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream); // Scriem fișierul pe disk
            }

            // Construim URL-ul absolut cu schema și host-ul cererii curente (ex: https://api.lifealertplus.com)
            var request  = HttpContext.Request;
            var baseUrl  = $"{request.Scheme}://{request.Host}";
            var absoluteUrl = $"{baseUrl}/profile-images/{fileName}";
            user.ProfilePictureUrl = absoluteUrl;
            user.UpdatedAt         = DateTime.UtcNow;
            var result = await _userService.UpdateUserAsync(user);
            if (!result)
                return StatusCode(500, new { Message = "Failed to update user profile image." });

            return Ok(new { Message = "Profile image uploaded successfully.", ImageUrl = absoluteUrl });
        }

        // GET /api/user/{id} — Returnează profilul complet al utilizatorului
        // Numai utilizatorul sau un admin pot citi profilul
        [HttpGet("{id}")]
        public async Task<IActionResult> GetUserById(Guid id)
        {
            if (!CallerOwns(id) && !IsAdminRole())
                return Forbid();

            var user = await _userService.GetUserByIdAsync(id);
            if (user == null)
                return NotFound(new { Message = "User not found." });

            return Ok(new UserProfileDTO
            {
                Id                    = user.Id,
                FirstName             = user.FirstName,
                LastName              = user.LastName,
                Email                 = user.Email,
                PhoneNumber           = user.PhoneNumber,
                ProfilePictureUrl     = user.ProfilePictureUrl,
                IsEmailConfirmed      = user.IsEmailConfirmed,
                Provider              = string.IsNullOrEmpty(user.Provider) ? "Local" : user.Provider, // "Local" sau "Google"
                FirstDayOfTheWeek     = user.FirstDayOfTheWeek,
                Language              = user.Language ?? "en",
                MinHeartRate          = user.MinHeartRate  ?? 0,
                MaxHeartRate          = user.MaxHeartRate  ?? 0,
                MinTemperature        = (float)(user.MinTemperature ?? 0),
                MaxTemperature        = (float)(user.MaxTemperature ?? 0),
                MinSpO2               = user.MinSpO2       ?? 0,
                MaxSpO2               = user.MaxSpO2       ?? 0,
                UpdateFrequency       = user.UpdateFrequency ?? 30, // Secunde între citirile ESP (default 30s)
                NotifyByEmail         = user.NotifyByEmail,
                NotifyByPush          = user.NotifyByPush,
                NotifyBySms           = user.NotifyBySms,
                EnableDailyReport     = user.EnableDailyReport,
                LastChangedPasswordAt = user.LastChangedPasswordAt,
                CreatedAt             = user.CreatedAt,
                UpdatedAt             = user.UpdatedAt
            });
        }

        // GET /api/user/email/{email} — Returnează profilul utilizatorului după email
        // Numai utilizatorul cu acel email poate accesa (autorizare după email din JWT)
        [HttpGet("email/{email}")]
        public async Task<IActionResult> GetUserByEmail(string email)
        {
            if (!CallerOwn(email))
                return Forbid();

            var user = await _userService.GetUserByEmailAsync(email);
            if (user == null)
                return NotFound(new { Message = "User not found." });

            return Ok(new UserProfileDTO
            {
                Id                    = user.Id,
                FirstName             = user.FirstName,
                LastName              = user.LastName,
                Email                 = user.Email,
                PhoneNumber           = user.PhoneNumber,
                ProfilePictureUrl     = user.ProfilePictureUrl,
                IsEmailConfirmed      = user.IsEmailConfirmed,
                Provider              = string.IsNullOrEmpty(user.Provider) ? "Local" : user.Provider,
                FirstDayOfTheWeek     = user.FirstDayOfTheWeek,
                Language              = user.Language ?? "en",
                MinHeartRate          = user.MinHeartRate  ?? 0,
                MaxHeartRate          = user.MaxHeartRate  ?? 0,
                MinTemperature        = (float)(user.MinTemperature ?? 0),
                MaxTemperature        = (float)(user.MaxTemperature ?? 0),
                MinSpO2               = user.MinSpO2       ?? 0,
                MaxSpO2               = user.MaxSpO2       ?? 0,
                UpdateFrequency       = user.UpdateFrequency ?? 30,
                NotifyByEmail         = user.NotifyByEmail,
                NotifyByPush          = user.NotifyByPush,
                NotifyBySms           = user.NotifyBySms,
                EnableDailyReport     = user.EnableDailyReport,
                LastChangedPasswordAt = user.LastChangedPasswordAt,
                CreatedAt             = user.CreatedAt,
                UpdatedAt             = user.UpdatedAt
            });
        }

        // GET /api/user — Lista tuturor utilizatorilor (Admin only)
        // Exclude administratorii din lista returnată (se afișează doar utilizatori normali)
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAllUsers()
        {
            var users    = await _userService.GetAllUsersAsync();
            var response = new List<UserListItemDTO>();

            foreach (var user in users)
            {
                if (IsAdminRole(user.Role?.Name))
                    continue; // Excludem administratorii din lista returnată

                response.Add(new UserListItemDTO
                {
                    Id                    = user.Id,
                    FirstName             = user.FirstName,
                    LastName              = user.LastName,
                    Email                 = user.Email,
                    ProfilePictureUrl     = user.ProfilePictureUrl,
                    IsEmailConfirmed      = user.IsEmailConfirmed,
                    Provider              = user.Provider ?? "Local",
                    Role                  = user.Role?.Name ?? "User",
                    CreatedAt             = user.CreatedAt,
                    UpdatedAt             = user.UpdatedAt,
                    DeletedAt             = user.DeletedAt,
                    LastChangedPasswordAt = user.LastChangedPasswordAt
                });
            }

            return Ok(response);
        }

        // POST /api/user/{id}/consent — Înregistrează consimțământul GDPR al utilizatorului
        // Apelat la primul login Google (utilizatorul acceptă termenii și condițiile de procesare a datelor)
        [HttpPost("{id}/consent")]
        public async Task<IActionResult> RecordConsent(Guid id)
        {
            if (!CallerOwns(id)) return Forbid();
            var user = await _userService.GetUserByIdAsync(id);
            if (user == null) return NotFound();
            if (user.DataProcessingConsentAt != null)
                return Ok(new { Message = "Consent already recorded." }); // Nu înregistrăm de mai multe ori
            user.DataProcessingConsentAt = DateTime.UtcNow; // Momentul consimțământului (GDPR)
            user.UpdatedAt               = DateTime.UtcNow;
            await _userService.UpdateUserAsync(user);
            _logger.LogInformation("GDPR consent recorded for user {UserId} (Google first login)", id);
            return Ok(new { Message = "Consent recorded." });
        }

        // GET /api/user/{id}/gdpr-export — Export GDPR Art. 20: portabilitatea datelor
        // Exportă TOATE datele personale ale utilizatorului și ale persoanelor monitorizate
        // ca fișier JSON descărcabil (dreptul utilizatorului de a-și lua datele)
        [HttpGet("{id}/gdpr-export")]
        public async Task<IActionResult> GdprExport(Guid id)
        {
            if (!CallerOwns(id)) return Forbid();

            var user = await _userService.GetUserByIdAsync(id);
            if (user == null) return NotFound();

            // Colectăm toate datele asociate utilizatorului
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

            // Construim structura de export (exportul conține DOAR câmpurile necesare, fără date tehnice interne)
            var export = new
            {
                ExportedAt = DateTime.UtcNow,
                GdprNote   = "This file contains all personal data held by LifeAlertPlus about you and the persons you monitor, exported under GDPR Art. 20 (right to data portability).",
                Account    = new
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
                    m.Id, m.FirstName, m.LastName, m.Birthdate, m.Gender,
                    m.Address, m.DeviceSerialNumber, m.DataRetentionDays,
                    m.ArchiveRetentionDays, m.IsArchived, m.ArchivedAt, m.CreatedAt
                }),
                Measurements = measurements.Select(m => new
                {
                    m.IdMonitored, m.Pulse, m.Temperature, m.SpO2,
                    m.IsFall, m.Activity, m.Coordinates, m.CreatedAt
                }),
                Notifications = notifications.Select(n => new
                {
                    n.IdMonitored, n.NotificationType, n.Message, n.IsRead, n.CreatedAt
                }),
                ActivityProfiles = activityProfiles.Select(ap => new
                {
                    ap.IdMonitored, ap.HourOfDay, ap.AveragePulse,
                    ap.MovementRate, ap.SleepProbability, ap.DataPoints, ap.LastUpdated
                })
            };

            // Serializăm cu indentat pentru lizibilitate umană
            var json     = System.Text.Json.JsonSerializer.Serialize(export,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            var bytes    = System.Text.Encoding.UTF8.GetBytes(json);
            var fileName = $"lifealertplus-gdpr-export-{DateTime.UtcNow:yyyyMMdd}.json";

            _logger.LogInformation("GDPR export generated for user {UserId}", id);
            return File(bytes, "application/json", fileName); // Descărcare directă în browser
        }

        // DELETE /api/user/delete/{id} — GDPR Art. 17: dreptul la ștergere (dreptul de a fi uitat)
        // Șterge permanent TOATE datele: contul, persoanele monitorizate exclusive, notificările
        // Persoanele monitorizate PARTAJATE cu alți utilizatori nu sunt șterse — se elimină doar legătura
        [HttpDelete("delete/{id}")]
        public async Task<IActionResult> DeleteUser(Guid id)
        {
            if (!CallerOwns(id) && !IsAdminRole()) return Forbid();

            var user = await _userService.GetUserByIdAsync(id);
            if (user == null) return NotFound(new { Message = "User not found." });

            TryDeleteProfileImageFile(user.ProfilePictureUrl); // Ștergem imaginea de profil de pe disk

            // Procesăm legăturile User-Monitored pentru a decide ce se șterge
            var ownedLinks = await _db.UserMonitoreds
                .Where(um => um.IdUser == id)
                .ToListAsync();

            foreach (var link in ownedLinks)
            {
                // Verificăm câți alți utilizatori mai au acces la această persoană monitorizată
                var otherOwners = await _db.UserMonitoreds
                    .CountAsync(um => um.IdMonitored == link.IdMonitored && um.IdUser != id);

                if (otherOwners == 0)
                {
                    // Nici un alt îngrijitor — ștergem persoana complet (cascade va șterge și datele)
                    var monitored = await _db.Monitoreds.FindAsync(link.IdMonitored);
                    if (monitored != null)
                        _db.Monitoreds.Remove(monitored);
                }
                else
                {
                    // Persoana e partajată — eliminăm doar legătura cu acest utilizator
                    _db.UserMonitoreds.Remove(link);
                }
            }

            // Ștergem notificările utilizatorului pentru persoanele care rămân în sistem (partajate)
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

            // Logăm în audit cu distincția: utilizatorul s-a șters singur sau un admin a șters contul
            var byAdmin  = IsAdminRole() && !CallerOwns(id);
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

        // PATCH /api/user/{id}/daily-report — Activează/dezactivează raportul zilnic pentru utilizator
        [HttpPatch("{id}/daily-report")]
        public async Task<IActionResult> ToggleDailyReport(Guid id, [FromBody] ToggleDailyReportRequest request)
        {
            if (!CallerOwns(id))
                return Forbid();

            var user = await _userService.GetUserByIdAsync(id);
            if (user == null)
                return NotFound();

            user.EnableDailyReport = request.Enabled; // true = activat, false = dezactivat
            await _userService.UpdateUserAsync(user);

            return Ok(new { EnableDailyReport = request.Enabled });
        }
    }
}