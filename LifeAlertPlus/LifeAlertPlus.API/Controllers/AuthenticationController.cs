using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Shared.DTOs.Requests.User;
using LifeAlertPlus.Shared.DTOs.Responses.User;
using Microsoft.AspNetCore.Authorization;
using LifeAlertPlus.API.Services;
using System.Security.Claims;

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
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthenticationController> _logger;
        private readonly GetUrlService _getUrlService;
        private readonly IRoleService _roleService;

        public AuthenticationController(IUserService userService, IConfiguration configuration, IAuthenticationService authenticationService, IJwtService jwtService, IEmailService emailService, ILogger<AuthenticationController> logger, GetUrlService getUrlService, IRoleService roleService)
        {
            _userService = userService;
            _configuration = configuration;
            _authenticationService = authenticationService;
            _jwtService = jwtService;
            _emailService = emailService;
            _logger = logger;
            _getUrlService = getUrlService;
            _roleService = roleService;
        }

        [HttpPost("login")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> Login([FromBody] UserLoginRequestDTO request)
        {
            var user = await _userService.GetUserByEmailAsync(request.Email);
            if (user == null)
            {
                return Unauthorized(new UserLoginResponseDTO { Success = false, Message = "No account found with this email address.", Token = string.Empty });
            }
            if (string.IsNullOrEmpty(request.Password) || string.IsNullOrEmpty(user.PasswordHash) ||
                !_authenticationService.VerifyPassword(request.Password, user.PasswordHash))
            {
                return Unauthorized(new UserLoginResponseDTO { Success = false, Message = "Incorrect password.", Token = string.Empty });
            }
            if (!user.IsEmailConfirmed)
            {
                return Unauthorized(new UserLoginResponseDTO { Success = false, Message = "Please verify your email before logging in.", Token = string.Empty });
            }
            if(user.DeletedAt != null)
            {
                user.DeletedAt = null;
                await _userService.UpdateUserAsync(user);
            }

            var roleName = (await _roleService.GetByIdAsync(user.RoleId))?.Name ?? "User";
            var isAdmin = string.Equals(roleName, "Admin", StringComparison.OrdinalIgnoreCase);

            var token = _jwtService.GenerateToken(user, roleName);

            return Ok(new UserLoginResponseDTO
            {
                Success = true,
                Message = "Login successful.",
                Token = token,
                IsAdmin = isAdmin

            });
        }

        [HttpPost("register")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> Register([FromBody] UserRegisterRequestDTO request)
        {
            var existingUser = await _userService.GetUserByEmailAsync(request.Email);
            if (existingUser != null)
            {
                return Conflict(new UserResponseDTO { Success = false, Message = "An account with this email address already exists." });
            }

            if (!string.IsNullOrWhiteSpace(request.PhoneNumber))
            {
                var existingPhone = await _userService.GetUserByPhoneNumberAsync(request.PhoneNumber.Trim());
                if (existingPhone != null)
                {
                    return Conflict(new UserResponseDTO { Success = false, Message = "This phone number is already associated with another account." });
                }
            }

            var passwordValidation = await _authenticationService.VerifyPassword(request.Password);
            if (!passwordValidation.Success)
            {
                return BadRequest(passwordValidation);
            }

            var created = await _userService.CreateUserAsync(request);
            if(!created)
            {
                return BadRequest(new UserResponseDTO { Success = false, Message = "Registration failed." });
            }

            try
            {
                var newUser = await _userService.GetUserByEmailAsync(request.Email);
                if (newUser != null && !string.IsNullOrEmpty(newUser.EmailConfirmationToken))
                {
                    var verificationUrl = $"{_getUrlService.GetApiBaseUrl()}/api/authentication/verify-email?token={Uri.EscapeDataString(newUser.EmailConfirmationToken)}";
                    var userName = $"{request.FirstName} {request.LastName}";
                    await _emailService.SendRegistrationSuccessEmailAsync(request.Email, userName, verificationUrl);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send registration email for {Email}", request.Email);
            }

            return Ok(new UserResponseDTO { Success = true, Message = "Registration successful." });
        }

        [HttpGet("verify-email")]
        public async Task<IActionResult> VerifyEmail([FromQuery] string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return Redirect($"{_getUrlService.GetClientBaseUrl()}/login?verified=false&reason=invalid");
            }

            var user = await _userService.VerifyEmailAsync(token);
            if (user == null)
            {
                return Redirect($"{_getUrlService.GetClientBaseUrl()}/login?verified=false&reason=expired");
            }

            return Redirect($"{_getUrlService.GetClientBaseUrl()}/login?verified=true");
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] UserResetPasswordRequestDTO request)
        {
            if (string.IsNullOrEmpty(request.Token) || string.IsNullOrEmpty(request.NewPassword) || string.IsNullOrEmpty(request.ConfirmPassword))
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

            if(!user.IsEmailConfirmed)
            {
                return BadRequest("Email not confirmed.");
            }

            await _userService.PasswordChangeAsync(user, request.NewPassword);

            return Ok("Password has been reset successfully.");
        }

        [HttpPost("forgot-password")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> ForgotPassword([FromBody] UserForgotPasswordRequestDTO request)
        {
            if (string.IsNullOrEmpty(request.Email))
            {
                return BadRequest("Email is required.");
            }

            var user = await _userService.GetUserByEmailAsync(request.Email);
            if (user == null)
            {
                return BadRequest(new UserResponseDTO { Success = false, Message = "This email is not registered." });
            }
            if(user.Provider != "Local")
            {
                return BadRequest(new UserResponseDTO { Success = false, Message = "This email is registered via a third-party provider. Password reset is not applicable." });
            }

            if(!user.IsEmailConfirmed)
            {
                return BadRequest(new UserResponseDTO { Success = false, Message = "Email not confirmed. Cannot reset password." });
            }

            await _userService.InitiatePasswordResetAsync(user);

            try
            {
                var resetUrl = $"{_getUrlService.GetClientBaseUrl()}/reset-password?token={Uri.EscapeDataString(user.PasswordResetToken!)}";
                var userName = $"{user.FirstName} {user.LastName}";
                await _emailService.SendPasswordResetEmailAsync(user.Email, userName, resetUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send password reset email for {Email}", request.Email);
            }

            return Ok(new UserResponseDTO { Success = true, Message = "If the email exists, a password reset link has been sent." });
        }

        [Authorize]
        [HttpPatch("change-email")]
        public async Task<IActionResult> ChangeEmail([FromBody] UserChangeEmailRequestDTO request)
        {
            var callerEmail = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email");
            if (string.IsNullOrEmpty(callerEmail))
                return Unauthorized(new UserResponseDTO { Success = false, Message = "Unable to determine authenticated user." });

            // Override with the identity from the JWT — prevents modifying another user's email
            request.CurrentEmail = callerEmail;

            var emailChangeValidation = await _authenticationService.ValidateChangeEmail(request);
            if (!emailChangeValidation.Success) {
                return BadRequest(emailChangeValidation);
            }

            var user = await _userService.GetUserByEmailAsync(request.CurrentEmail);
            if (user == null)
            {
                return NotFound(new UserResponseDTO { Success = false, Message = "User with the current email does not exist." });
            }

            var existingUserWithNewEmail = await _userService.GetUserByEmailAsync(request.NewEmail);
            if (existingUserWithNewEmail != null)
            {
                return Conflict(new UserResponseDTO { Success = false, Message = "The new email address is already in use." });
            }

            if (string.IsNullOrEmpty(user.PasswordHash) || 
                !_authenticationService.VerifyPassword(request.CurrentPassword, user.PasswordHash))
            {
                return BadRequest(new UserResponseDTO { Success = false, Message = "Invalid password." });
            }

            await _userService.InitiateEmailChangeAsync(user, request.NewEmail);

            try
            {
                var userName = $"{user.FirstName} {user.LastName}";

                var verificationUrl = $"{_getUrlService.GetApiBaseUrl()}/api/authentication/verify-email-change?token={Uri.EscapeDataString(user.EmailChangeToken!)}";
                await _emailService.SendEmailChangeVerificationAsync(request.NewEmail, userName, verificationUrl, request.CurrentEmail);

                var cancelUrl = $"{_getUrlService.GetApiBaseUrl()}/api/authentication/cancel-email-change?token={Uri.EscapeDataString(user.EmailChangeCancelToken!)}";
                await _emailService.SendEmailChangeNotificationAsync(request.CurrentEmail, userName, request.NewEmail, cancelUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email change emails for {Email}", request.CurrentEmail);
            }

            return Ok(new UserUpdateEmailResponseDTO { Success = true, Message = "Email change initiated. Please check both your current and new email addresses.", RequiresLogout = true });
        }

        [HttpGet("verify-email-change")]
        public async Task<IActionResult> VerifyEmailChange([FromQuery] string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return BadRequest(new UserResponseDTO { Success = false, Message = "Token is required." });
            }

            var user = await _userService.GetUserByEmailChangeTokenAsync(token);

            if (user == null || user.EmailChangeExpires == null || user.EmailChangeExpires < DateTime.UtcNow)
            {
                return BadRequest(new UserResponseDTO { Success = false, Message = "Invalid or expired verification token." });
            }

            if (string.IsNullOrEmpty(user.EmailChangeCancelToken))
            {
                return BadRequest(new UserResponseDTO { Success = false, Message = "Email change request has been cancelled." });
            }

            await _userService.EmailChangeAsync(user);

            return Ok(new UserResponseDTO { Success = true, Message = "Email address successfully changed and verified." });
        }

        [HttpGet("cancel-email-change")]
        public async Task<IActionResult> CancelEmailChange([FromQuery] string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return BadRequest(new UserResponseDTO { Success = false, Message = "Token is required." });
            }

            var user = await _userService.GetUserByEmailChangeCancelTokenAsync(token);

            if (user == null || user.EmailChangeExpires == null || user.EmailChangeExpires < DateTime.UtcNow)
            {
                return BadRequest(new UserResponseDTO { Success = false, Message = "Invalid or expired cancellation token." });
            }

            await _userService.CancelEmailChangeAsync(user);

            return Ok(new UserResponseDTO { Success = true, Message = "Email change request has been successfully cancelled. Your account is secure." });
        }

        [Authorize]
        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] UserChangePasswordRequestDTO request)
        {
            var callerEmail = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email");
            if (string.IsNullOrEmpty(callerEmail))
                return Unauthorized(new UserResponseDTO { Success = false, Message = "Unable to determine authenticated user." });

            var passwordValidation = await _authenticationService.ValidateChangePassword(request.CurrentPassword, request.NewPassword, request.ConfirmPassword);
            if (!passwordValidation.Success) {
                return BadRequest(passwordValidation);
            }

            var user = await _userService.GetUserByEmailAsync(callerEmail);

            if (user == null || string.IsNullOrEmpty(user.PasswordHash) || !_authenticationService.VerifyPassword(request.CurrentPassword, user.PasswordHash))
            {
                return BadRequest(new UserResponseDTO { Success = false, Message = "Invalid current password." });
            }

            await _userService.PasswordChangeAsync(user, request.NewPassword);

            return Ok(new UserResponseDTO { Success = true, Message = "Password has been changed successfully." });
        }
    }
}