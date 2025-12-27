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
    }
}