using Microsoft.AspNetCore.Mvc;
using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Infrastructure.Context;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UserController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly LifeAlertPlusDbContext _context;
        private readonly IConfiguration _configuration;

        public UserController(IUserService userService, LifeAlertPlusDbContext context, IConfiguration configuration)
        {
            _userService = userService;
            _context = context;
            _configuration = configuration;
        }

        [HttpPut("update/{id}")]
        public async Task<IActionResult> UpdateUser(Guid id, [FromBody] Domain.Entities.User updatedUser)
        {
            var user = await _userService.GetUserByIdAsync(id);
            if (user == null)
            {
                return NotFound(new { Message = "User not found." });
            }

            user.FirstName = updatedUser.FirstName;
            user.LastName = updatedUser.LastName;
            user.Email = updatedUser.Email;
            user.UpdatedAt = DateTime.UtcNow;

            var result = await _userService.UpdateUserAsync(user);
            if (!result)
            {
                return StatusCode(500, new { Message = "Failed to update user." });
            }

            return Ok(new { Message = "User updated successfully." });
        }

        [HttpPatch("deactivate/{id}")]
        public async Task<IActionResult> DeactivateUser(Guid id)
        {
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
            var user = await _userService.GetUserByIdAsync(id);
            if (user == null)
            {
                return NotFound(new { Message = "User not found." });
            }

            var result = await _userService.DeleteUserAsync(id);
            
            if (!result)
            {
                return StatusCode(500, new { Message = "Failed to delete user." });
            }

            return Ok(new { Message = "User deleted successfully." });
        }

        [HttpPost("upload-profile-image/{id}")]
        public async Task<IActionResult> UploadProfileImage(Guid id, IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { Message = "No file uploaded." });

            var user = await _userService.GetUserByIdAsync(id);
            if (user == null)
                return NotFound(new { Message = "User not found." });

            // Creează folderul dacă nu există
            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "profile-images");
            if (!Directory.Exists(uploadsFolder))
                Directory.CreateDirectory(uploadsFolder);

            // Nume unic pentru fișier
            var fileName = $"{id}_{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            var filePath = Path.Combine(uploadsFolder, fileName);
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Setează URL absolut pentru acces din client (ex: http://localhost:5176/profile-images/...) 
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
    }
}