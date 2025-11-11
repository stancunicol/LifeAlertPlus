using Microsoft.AspNetCore.Mvc;
using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Infrastructure.Context;
using LifeAlertPlus.Shared.DTOs.Requests.User;
using LifeAlertPlus.Shared.DTOs.Responses.User;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthentificationController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly LifeAlertPlusDbContext _dbContext;
        private readonly IConfiguration _configuration;

        public AuthentificationController(IUserService userService, LifeAlertPlusDbContext lifeAlertPlusDbContext, IConfiguration configuration)
        {
            _userService = userService;
            _dbContext = lifeAlertPlusDbContext;
            _configuration = configuration;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] UserLoginRequestDTO request)
        {
            var user = await _userService.GetUserByEmailAsync(request.Email);
            if (user == null || user.PasswordHash != request.Password)
            {
                return Ok(new UserLoginResponseDTO { Success = false, Message = "Login failed.", Token = string.Empty });
            }
            var token = "generated_jwt_token";
            return Ok(new UserLoginResponseDTO { Success = true, Message = "Login successfull.", Token = token });
        }
    }
}