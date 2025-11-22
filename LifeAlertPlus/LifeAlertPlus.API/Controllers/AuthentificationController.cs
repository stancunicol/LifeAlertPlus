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
        private readonly IAuthentificationService _authentificationService;
        private readonly IJwtService _jwtService;
        private readonly IEmailService _emailService;
        private readonly LifeAlertPlusDbContext _dbContext;
        private readonly IConfiguration _configuration;

        public AuthentificationController(IUserService userService, LifeAlertPlusDbContext lifeAlertPlusDbContext, IConfiguration configuration, IAuthentificationService authentificationService, LifeAlertPlusDbContext dbContext, IJwtService jwtService, IEmailService emailService)
        {
            _userService = userService;
            _dbContext = lifeAlertPlusDbContext;
            _configuration = configuration;
            _authentificationService = authentificationService;
            _dbContext = dbContext;
            _jwtService = jwtService;
            _emailService = emailService;
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

            try
            {
                // Point to the Blazor client login page, not the API
                var loginUrl = "http://localhost:5254/login";
                var userName = $"{request.FirstName} {request.LastName}";
                await _emailService.SendRegistrationSuccessEmailAsync(request.Email, userName, loginUrl);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send registration email: {ex.Message}");
            }

            return Ok(new UserRegisterResponseDTO { Success = true, Message = "Registration successful." });
        }
    }
}