using Microsoft.AspNetCore.Mvc;
using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Infrastructure.Context;
using LifeAlertPlus.Shared.DTOs.Requests.User;
using LifeAlertPlus.Shared.DTOs.Responses.User;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthenticationController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IAuthenticationService _authenticationService;
        private readonly IJwtService _jwtService;
        private readonly IEmailService _emailService;
        private readonly LifeAlertPlusDbContext _dbContext;
        private readonly IConfiguration _configuration;

        public AuthenticationController(IUserService userService, LifeAlertPlusDbContext lifeAlertPlusDbContext, IConfiguration configuration, IAuthenticationService authenticationService, LifeAlertPlusDbContext dbContext, IJwtService jwtService, IEmailService emailService)
        {
            _userService = userService;
            _dbContext = lifeAlertPlusDbContext;
            _configuration = configuration;
            _authenticationService = authenticationService;
            _dbContext = dbContext;
            _jwtService = jwtService;
            _emailService = emailService;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] UserLoginRequestDTO request)
        {
            var user = await _userService.GetUserByEmailAsync(request.Email);

            if (user == null || string.IsNullOrEmpty(request.Password) || string.IsNullOrEmpty(user.PasswordHash) || !_authenticationService.VerifyPassword(request.Password, user.PasswordHash))
            {
                return Ok(new UserLoginResponseDTO { Success = false, Message = "Login failed.", Token = string.Empty });
            }

            if(user.IsEmailConfirmed == false)
            {
                return Ok(new UserLoginResponseDTO { Success = false, Message = "Email not confirmed.", Token = string.Empty });
            }

            if(user.DeletedAt != null)
            {
                user.DeletedAt = null;
                await _userService.UpdateUserAsync(user);
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

            if(string.IsNullOrEmpty(request.Password))
            {
                return Ok(new UserRegisterResponseDTO { Success = false, Message = "Password is required." });
            }

            if(request.Password.Length < 8)
            {
                return Ok(new UserRegisterResponseDTO { Success = false, Message = "Password must be at least 8 characters long." });
            }

            if(request.Password.All(char.IsLower) || request.Password.All(char.IsUpper) || !request.Password.Any(char.IsDigit) || !request.Password.Any(ch => !char.IsLetterOrDigit(ch)))
            {
                return Ok(new UserRegisterResponseDTO { Success = false, Message = "Password must contain uppercase, lowercase, digit, and special character." });
            }

            var response = await _userService.CreateUserAsync(request);

            if(response == false)
            {
                return Ok(new UserRegisterResponseDTO { Success = false, Message = "Registration failed." });
            }

            try
            {
                var newUser = await _userService.GetUserByEmailAsync(request.Email);
                if (newUser != null && !string.IsNullOrEmpty(newUser.EmailConfirmationToken))
                {
                    var verificationUrl = $"http://localhost:5176/api/authentification/verify-email?token={Uri.EscapeDataString(newUser.EmailConfirmationToken)}";
                    var userName = $"{request.FirstName} {request.LastName}";
                    await _emailService.SendRegistrationSuccessEmailAsync(request.Email, userName, verificationUrl);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send registration email: {ex.Message}");
            }

            return Ok(new UserRegisterResponseDTO { Success = true, Message = "Registration successful." });
        }

        [HttpGet("verify-email")]
        public async Task<IActionResult> VerifyEmail([FromQuery] string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return Redirect("http://localhost:5254/login?verified=false&reason=invalid");
            }

            var user = await _userService.VerifyEmailAsync(token);

            if (user == null)
            {
                return Redirect("http://localhost:5254/login?verified=false&reason=expired");
            }

            return Redirect("http://localhost:5254/login?verified=true");
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] UserResetPasswordRequestDTO request)
        {
            if (string.IsNullOrEmpty(request.Token) || string.IsNullOrEmpty(request.NewPassword) || string.IsNullOrEmpty(request.ConfirmPassword) || request.NewPassword != request.ConfirmPassword)
            {
                return BadRequest("Invalid token or password.");
            }

            var user = await _userService.GetUserByResetTokenAsync(request.Token);

            if (user == null || user.PasswordResetExpires == null || user.PasswordResetExpires < DateTime.UtcNow)
            {
                return BadRequest("Invalid or expired token.");
            }

            if(request.NewPassword != request.ConfirmPassword)
            {
                return BadRequest("Passwords do not match.");
            }

            if(user.IsEmailConfirmed == false)
            {
                return BadRequest("Email not confirmed.");
            }

            user.PasswordHash = _authenticationService.HashPassword(request.NewPassword);
            user.PasswordResetToken = null;
            user.PasswordResetExpires = null;
            user.UpdatedAt = DateTime.UtcNow;
            user.LastChangedPasswordAt = DateTime.UtcNow;

            await _userService.UpdateUserAsync(user);

            return Ok("Password has been reset successfully.");
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] UserForgotPasswordRequestDTO request)
        {
            if (string.IsNullOrEmpty(request.Email))
            {
                return BadRequest("Email is required.");
            }

            var user = await _userService.GetUserByEmailAsync(request.Email);

            if (user == null)
            {
                return Ok(new { Success = false, Message = "This email is not registered." });
            }
            else 
            {
                if(user.Provider != "Local")
                {
                    return Ok(new { Success = false, Message = "This email is registered via a third-party provider. Password reset is not applicable." });
                }

                if(user.IsEmailConfirmed == false)
                {
                    return Ok(new { Success = false, Message = "Email not confirmed. Cannot reset password." });
                }
            }

            var resetToken = _userService.GeneratePasswordResetToken();
            user.PasswordResetToken = resetToken;
            user.PasswordResetExpires = DateTime.UtcNow.AddHours(24);
            user.UpdatedAt = DateTime.UtcNow;

            await _userService.UpdateUserAsync(user);

            try
            {
                var resetUrl = $"http://localhost:5254/reset-password?token={Uri.EscapeDataString(resetToken)}";
                var userName = $"{user.FirstName} {user.LastName}";
                await _emailService.SendPasswordResetEmailAsync(user.Email, userName, resetUrl);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send password reset email: {ex.Message}");
            }

            return Ok(new { Success = true, Message = "If the email exists, a password reset link has been sent." });
        }

        [HttpPatch("change-email")]
        public async Task<IActionResult> ChangeEmail([FromBody] UserChangeEmailRequestDTO request)
        {
            if (string.IsNullOrEmpty(request.CurrentPassword) || string.IsNullOrEmpty(request.NewEmail) || string.IsNullOrEmpty(request.ConfirmEmail) || request.NewEmail !=  request.ConfirmEmail)
            {
                return Ok(new UserUpdateEmailResponseDTO { Success = false, Message = "All fields are required and new email must match confirmation." });
            }

            var user = await _userService.GetUserByEmailAsync(request.CurrentEmail);

            if (user == null)
            {
                return Ok(new UserUpdateEmailResponseDTO { Success = false, Message = "User with the current email does not exist." });
            }

            var existingUserWithNewEmail = await _userService.GetUserByEmailAsync(request.NewEmail);
            if (existingUserWithNewEmail != null)
            {
                return Ok(new UserUpdateEmailResponseDTO { Success = false, Message = "The new email address is already in use." });
            }

            var verificationToken = _userService.GenerateEmailVerificationToken();
            var cancelToken = _userService.GenerateEmailChangeCancelToken();

            user.PendingEmail = request.NewEmail;
            user.EmailChangeToken = verificationToken;
            user.EmailChangeExpires = DateTime.UtcNow.AddHours(24);
            user.EmailChangeCancelToken = cancelToken;
            user.UpdatedAt = DateTime.UtcNow;

            await _userService.UpdateUserAsync(user);

            try
            {
                var userName = $"{user.FirstName} {user.LastName}";
                
                var verificationUrl = $"http://localhost:5176/api/authentification/verify-email-change?token={Uri.EscapeDataString(verificationToken)}";
                await _emailService.SendEmailChangeVerificationAsync(request.NewEmail, userName, verificationUrl, request.CurrentEmail);

                var cancelUrl = $"http://localhost:5176/api/authentification/cancel-email-change?token={Uri.EscapeDataString(cancelToken)}";
                await _emailService.SendEmailChangeNotificationAsync(request.CurrentEmail, userName, request.NewEmail, cancelUrl);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send email change verification email: {ex.Message}");
            }

            return Ok(new UserUpdateEmailResponseDTO { Success = true, Message = "Email change initiated. Please check both your current and new email addresses.", RequiresLogout = true });
        }

        [HttpGet("verify-email-change")]
        public async Task<IActionResult> VerifyEmailChange([FromQuery] string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return BadRequest("Token is required.");
            }

            var users = await _userService.GetAllUsersAsync();
            var user = users.FirstOrDefault(u => u.EmailChangeToken == token);

            if (user == null || user.EmailChangeExpires == null || user.EmailChangeExpires < DateTime.UtcNow)
            {
                return BadRequest("Invalid or expired verification token.");
            }

            if (string.IsNullOrEmpty(user.EmailChangeCancelToken))
            {
                return BadRequest("Email change request has been cancelled.");
            }

            user.Email = user.PendingEmail;
            user.IsEmailConfirmed = true;
            user.EmailChangeToken = null;
            user.EmailChangeExpires = null;
            user.EmailChangeCancelToken = null;
            user.PendingEmail = null;
            user.UpdatedAt = DateTime.UtcNow;

            await _userService.UpdateUserAsync(user);

            return Ok("Email address successfully changed and verified.");
        }

        [HttpGet("cancel-email-change")]
        public async Task<IActionResult> CancelEmailChange([FromQuery] string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return BadRequest("Token is required.");
            }

            var user = await _userService.GetUserByEmailChangeCancelTokenAsync(token);

            if (user == null || user.EmailChangeExpires == null || user.EmailChangeExpires < DateTime.UtcNow)
            {
                return BadRequest("Invalid or expired cancellation token.");
            }

            user.EmailChangeToken = null;
            user.EmailChangeExpires = null;
            user.EmailChangeCancelToken = null;
            user.PendingEmail = null;
            user.UpdatedAt = DateTime.UtcNow;

            await _userService.UpdateUserAsync(user);

            return Ok("Email change request has been successfully cancelled. Your account is secure.");
        }

        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] UserChangePasswordRequestDTO request)
        {
            if (string.IsNullOrEmpty(request.CurrentPassword) || string.IsNullOrEmpty(request.NewPassword) || string.IsNullOrEmpty(request.ConfirmPassword))
            {
                return BadRequest("All fields are required.");
            }

            if (request.NewPassword != request.ConfirmPassword)
            {
                return BadRequest("New password and confirmation do not match.");
            }

            if(request.CurrentPassword == request.NewPassword)
            {
                return BadRequest("New password must be different from the current password.");
            }

            var user = await _userService.GetUserByEmailAsync(request.Email);

            if (user == null || !_authenticationService.VerifyPassword(request.CurrentPassword, user.PasswordHash))
            {
                return BadRequest("Invalid email or current password.");
            }

            user.PasswordHash = _authenticationService.HashPassword(request.NewPassword);
            user.UpdatedAt = DateTime.UtcNow;
            user.LastChangedPasswordAt = DateTime.UtcNow;

            await _userService.UpdateUserAsync(user);

            return Ok("Password has been changed successfully.");
        }
    }
}