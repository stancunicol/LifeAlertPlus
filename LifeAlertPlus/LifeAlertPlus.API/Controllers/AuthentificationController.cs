using Microsoft.AspNetCore.Mvc;
using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Infrastructure.Context;
using LifeAlertPlus.Shared.DTOs.Requests.User;
using LifeAlertPlus.Shared.DTOs.Responses.User;
using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Application.Services;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthentificationController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IAuthentificationService _authentificationService;
        private readonly IJwtService _jwtService;
        private readonly LifeAlertPlusDbContext _dbContext;
        private readonly IConfiguration _configuration;

        public AuthentificationController(IUserService userService, LifeAlertPlusDbContext lifeAlertPlusDbContext, IConfiguration configuration, IAuthentificationService authentificationService, LifeAlertPlusDbContext dbContext, IJwtService jwtService)
        {
            _userService = userService;
            _dbContext = lifeAlertPlusDbContext;
            _configuration = configuration;
            _authentificationService = authentificationService;
            _dbContext = dbContext;
            _jwtService = jwtService;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] UserLoginRequestDTO request)
        {
            var user = await _userService.GetUserByEmailAsync(request.Email);

            if (user == null || !_authentificationService.VerifyPassword(request.Password, user.PasswordHash))
            {
                return Ok(new UserLoginResponseDTO { Success = false, Message = "Login failed.", Token = string.Empty });
            }
            var token = _jwtService.GenerateToken(user);

            return Ok(new UserLoginResponseDTO
            {
                Success = true,
                Message = "Login successful.",
                Token = token
            });
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] UserRegisterRequestDTO request)
        {
            var existingUser = await _userService.GetUserByEmailAsync(request.Email);

            if (existingUser != null)
            {
                return Ok(new UserRegisterResponseDTO { Success = false, Message = "Email already in use." });
            }

            var response = await _userService.CreateUserAsync(request);

            if(response == false)
            {
                return Ok(new UserRegisterResponseDTO { Success = false, Message = "Registration failed." });
            }

            return Ok(new UserRegisterResponseDTO { Success = true, Message = "Registration successful." });
        }
    }
}