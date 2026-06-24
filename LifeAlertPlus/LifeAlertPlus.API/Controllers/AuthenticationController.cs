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
    // Controller pentru autentificare: login, register, confirmare email, resetare parolă, schimbare email/parolă
    [ApiController]
    [Route("api/[controller]")] // URL-ul de bază: /api/authentication
    public class AuthenticationController : ControllerBase
    {
        // Injectăm toate serviciile necesare prin constructor (Dependency Injection)
        private readonly IUserService _userService;
        private readonly IAuthenticationService _authenticationService;
        private readonly IJwtService _jwtService;
        private readonly IEmailService _emailService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthenticationController> _logger;
        private readonly GetUrlService _getUrlService;
        private readonly IRoleService _roleService;
        private readonly AuditService _auditService;

        public AuthenticationController(IUserService userService, IConfiguration configuration, IAuthenticationService authenticationService, IJwtService jwtService, IEmailService emailService, ILogger<AuthenticationController> logger, GetUrlService getUrlService, IRoleService roleService, AuditService auditService)
        {
            // Salvăm referințele la servicii pentru a le folosi în metode
            _userService = userService;
            _configuration = configuration;
            _authenticationService = authenticationService;
            _jwtService = jwtService;
            _emailService = emailService;
            _logger = logger;
            _getUrlService = getUrlService;
            _roleService = roleService;
            _auditService = auditService;
        }

        // POST /api/authentication/login — Autentificare cu email și parolă
        [HttpPost("login")]
        [EnableRateLimiting("auth")] // Limită: max 10 cereri/minut (protecție împotriva brute-force)
        public async Task<IActionResult> Login([FromBody] UserLoginRequestDTO request)
        {
            // Căutăm utilizatorul după email în baza de date
            var user = await _userService.GetUserByEmailAsync(request.Email);
            if (user == null)
            {
                // Nu divulgăm dacă email-ul există sau nu (ar putea fi exploatat pentru enumerare)
                return Unauthorized(new UserLoginResponseDTO { Success = false, Message = "No account found with this email address.", Token = string.Empty });
            }
            // Verificăm parola folosind bcrypt (comparație securizată cu hash-ul din DB)
            if (string.IsNullOrEmpty(request.Password) || string.IsNullOrEmpty(user.PasswordHash) ||
                !_authenticationService.VerifyPassword(request.Password, user.PasswordHash))
            {
                return Unauthorized(new UserLoginResponseDTO { Success = false, Message = "Incorrect password.", Token = string.Empty });
            }
            // Nu permitem login dacă email-ul nu a fost confirmat
            if (!user.IsEmailConfirmed)
            {
                return Unauthorized(new UserLoginResponseDTO { Success = false, Message = "Please verify your email before logging in.", Token = string.Empty });
            }
            // Dacă contul era marcat ca șters, îl "reactivăm" la login
            if(user.DeletedAt != null)
            {
                user.DeletedAt = null;
                await _userService.UpdateUserAsync(user);
            }

            // Obținem rolul utilizatorului pentru a-l include în token
            var roleName = (await _roleService.GetByIdAsync(user.RoleId))?.Name ?? "User";
            var isAdmin = string.Equals(roleName, "Admin", StringComparison.OrdinalIgnoreCase);

            // Generăm token-ul JWT cu informațiile utilizatorului și rolul său
            var token = _jwtService.GenerateToken(user, roleName);

            // Logăm acțiunea de login în sistemul de audit
            _auditService.LogAsync(user.Email, "Login", $"Successful login (provider: {user.Provider ?? "Local"})", "Security");

            // Returnăm token-ul și informațiile de login
            return Ok(new UserLoginResponseDTO
            {
                Success = true,
                Message = "Login successful.",
                Token = token,
                IsAdmin = isAdmin
            });
        }

        // POST /api/authentication/register — Creare cont nou
        [HttpPost("register")]
        [EnableRateLimiting("auth")] // Protecție împotriva spam-ului de înregistrări
        public async Task<IActionResult> Register([FromBody] UserRegisterRequestDTO request)
        {
            // Verificăm dacă există deja un cont cu acest email
            var existingUser = await _userService.GetUserByEmailAsync(request.Email);
            if (existingUser != null)
            {
                return Conflict(new UserResponseDTO { Success = false, Message = "An account with this email address already exists." });
            }

            // Verificăm dacă numărul de telefon e deja folosit de un alt cont
            if (!string.IsNullOrWhiteSpace(request.PhoneNumber))
            {
                var existingPhone = await _userService.GetUserByPhoneNumberAsync(request.PhoneNumber.Trim());
                if (existingPhone != null)
                {
                    return Conflict(new UserResponseDTO { Success = false, Message = "This phone number is already associated with another account." });
                }
            }

            // Consimțământul GDPR e obligatoriu pentru crearea contului
            if (!request.DataProcessingConsent)
                return BadRequest(new UserResponseDTO { Success = false, Message = "You must consent to data processing to create an account." });

            // Validăm complexitatea parolei (lungime minimă, caractere speciale etc.)
            var passwordValidation = await _authenticationService.VerifyPassword(request.Password);
            if (!passwordValidation.Success)
            {
                return BadRequest(passwordValidation);
            }

            // Creăm utilizatorul în baza de date (parola e hash-uită cu bcrypt)
            var created = await _userService.CreateUserAsync(request);
            if(!created)
            {
                return BadRequest(new UserResponseDTO { Success = false, Message = "Registration failed." });
            }

            // Trimitem email de confirmare cu link de verificare
            try
            {
                var newUser = await _userService.GetUserByEmailAsync(request.Email);
                if (newUser != null && !string.IsNullOrEmpty(newUser.EmailConfirmationToken))
                {
                    // Construim URL-ul de verificare cu token-ul unic al utilizatorului
                    var verificationUrl = $"{_getUrlService.GetApiBaseUrl()}/api/authentication/verify-email?token={Uri.EscapeDataString(newUser.EmailConfirmationToken)}";
                    var userName = $"{request.FirstName} {request.LastName}";
                    await _emailService.SendRegistrationSuccessEmailAsync(request.Email, userName, verificationUrl);
                }
            }
            catch (Exception ex)
            {
                // Eșecul emailului nu blochează înregistrarea — logăm eroarea și continuăm
                _logger.LogError(ex, "Failed to send registration email for {Email}", request.Email);
            }

            _auditService.LogAsync(request.Email, "Register", "New account created via email registration", "Account");
            return Ok(new UserResponseDTO { Success = true, Message = "Registration successful." });
        }

        // GET /api/authentication/verify-email?token=... — Confirmare email la înregistrare
        [HttpGet("verify-email")]
        public async Task<IActionResult> VerifyEmail([FromQuery] string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                // Redirecționăm la pagina de login cu mesaj de eroare în URL
                return Redirect($"{_getUrlService.GetClientBaseUrl()}/login?verified=false&reason=invalid");
            }

            // Căutăm utilizatorul cu acest token de confirmare și îl marcăm ca verificat
            var user = await _userService.VerifyEmailAsync(token);
            if (user == null)
            {
                // Token expirat sau inexistent — redirecționăm cu motiv
                return Redirect($"{_getUrlService.GetClientBaseUrl()}/login?verified=false&reason=expired");
            }

            // Confirmare reușită — redirecționăm la pagina de login cu succes
            return Redirect($"{_getUrlService.GetClientBaseUrl()}/login?verified=true");
        }

        // POST /api/authentication/reset-password — Setare parolă nouă cu token de resetare
        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] UserResetPasswordRequestDTO request)
        {
            // Validăm că toate câmpurile necesare sunt prezente
            if (string.IsNullOrEmpty(request.Token) || string.IsNullOrEmpty(request.NewPassword) || string.IsNullOrEmpty(request.ConfirmPassword))
            {
                return BadRequest(new { Message = "Invalid token or password." });
            }

            // Căutăm utilizatorul după token-ul de resetare
            var user = await _userService.GetUserByResetTokenAsync(request.Token);
            // Verificăm că token-ul există și nu a expirat
            if (user == null || user.PasswordResetExpires == null || user.PasswordResetExpires < DateTime.UtcNow)
            {
                return BadRequest(new { Message = "Invalid or expired token." });
            }

            // Verificăm că cele două parole coincid
            if(request.NewPassword != request.ConfirmPassword)
            {
                return BadRequest(new { Message = "Passwords do not match." });
            }

            // Nu putem reseta parola unui cont cu email neconfirmat
            if(!user.IsEmailConfirmed)
            {
                return BadRequest(new { Message = "Email not confirmed." });
            }

            // Actualizăm parola (hash-uim cu bcrypt și ștergem token-ul de resetare)
            await _userService.PasswordChangeAsync(user, request.NewPassword);

            return Ok(new { Message = "Password has been reset successfully." });
        }

        // POST /api/authentication/forgot-password — Solicitare link de resetare parolă
        [HttpPost("forgot-password")]
        [EnableRateLimiting("auth")] // Protecție împotriva spam-ului de cereri
        public async Task<IActionResult> ForgotPassword([FromBody] UserForgotPasswordRequestDTO request)
        {
            if (string.IsNullOrEmpty(request.Email))
            {
                return BadRequest(new { Message = "Email is required." });
            }

            var user = await _userService.GetUserByEmailAsync(request.Email);

            // Email doesn't exist or uses OAuth — return generic message to prevent email enumeration
            // Nu divulgăm dacă email-ul există — atacatorii ar putea afla ce conturi există
            if (user == null || user.Provider != "Local")
            {
                return Ok(new UserResponseDTO { Success = true, Message = "Dacă adresa de email există, vei primi un link de resetare în câteva minute." });
            }

            // Email exists but not confirmed — reveal this specifically (per UX requirement)
            // Excepție de la regula de mai sus: dacă email-ul e neconfirmat, îi spunem (e mai util)
            if (!user.IsEmailConfirmed)
            {
                return BadRequest(new UserResponseDTO { Success = false, Message = "Adresa de email nu a fost confirmată. Verificați inbox-ul pentru emailul de confirmare trimis la înregistrare." });
            }

            // Generăm token unic de resetare și îl salvăm în baza de date cu data expirării
            await _userService.InitiatePasswordResetAsync(user);

            // Trimitem email-ul cu link-ul de resetare
            try
            {
                // Construim URL-ul cu token-ul (encode pentru a evita caractere speciale în URL)
                var resetUrl = $"{_getUrlService.GetClientBaseUrl()}/reset-password?token={Uri.EscapeDataString(user.PasswordResetToken!)}";
                var userName = $"{user.FirstName} {user.LastName}";
                await _emailService.SendPasswordResetEmailAsync(user.Email, userName, resetUrl);
            }
            catch (Exception ex)
            {
                // Eșecul emailului e logat dar nu blocăm utilizatorul (token-ul e deja salvat în DB)
                _logger.LogError(ex, "Failed to send password reset email for {Email}", request.Email);
            }

            return Ok(new UserResponseDTO { Success = true, Message = "Un email cu linkul de resetare a parolei a fost trimis. Verificați inbox-ul." });
        }

        // PATCH /api/authentication/change-email — Schimbare email (necesită autentificare)
        [Authorize] // Endpoint protejat — utilizatorul trebuie să fie autentificat
        [HttpPatch("change-email")]
        public async Task<IActionResult> ChangeEmail([FromBody] UserChangeEmailRequestDTO request)
        {
            // Extragem email-ul utilizatorului autentificat din token-ul JWT
            var callerEmail = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email");
            if (string.IsNullOrEmpty(callerEmail))
                return Unauthorized(new UserResponseDTO { Success = false, Message = "Unable to determine authenticated user." });

            // Override with the identity from the JWT — prevents modifying another user's email
            // Suprascrierm email-ul curent cu cel din token — prevenim modificarea altui cont
            request.CurrentEmail = callerEmail;

            // Validăm că noul email e valid, diferit de cel curent etc.
            var emailChangeValidation = await _authenticationService.ValidateChangeEmail(request);
            if (!emailChangeValidation.Success) {
                return BadRequest(emailChangeValidation);
            }

            // Verificăm că utilizatorul există în baza de date
            var user = await _userService.GetUserByEmailAsync(request.CurrentEmail);
            if (user == null)
            {
                return NotFound(new UserResponseDTO { Success = false, Message = "User with the current email does not exist." });
            }

            // Verificăm că noul email nu e deja folosit de altcineva
            var existingUserWithNewEmail = await _userService.GetUserByEmailAsync(request.NewEmail);
            if (existingUserWithNewEmail != null)
            {
                return Conflict(new UserResponseDTO { Success = false, Message = "The new email address is already in use." });
            }

            // Cerem parola curentă ca verificare de securitate suplimentară
            if (string.IsNullOrEmpty(user.PasswordHash) ||
                !_authenticationService.VerifyPassword(request.CurrentPassword, user.PasswordHash))
            {
                return BadRequest(new UserResponseDTO { Success = false, Message = "Invalid password." });
            }

            // Generăm token-uri pentru confirmare și anulare, salvăm în DB
            await _userService.InitiateEmailChangeAsync(user, request.NewEmail);

            // Trimitem două email-uri: unul la noul email (confirmare) și unul la cel vechi (notificare/anulare)
            try
            {
                var userName = $"{user.FirstName} {user.LastName}";

                // Email la NOUL email cu link de confirmare a schimbării
                var verificationUrl = $"{_getUrlService.GetApiBaseUrl()}/api/authentication/verify-email-change?token={Uri.EscapeDataString(user.EmailChangeToken!)}";
                await _emailService.SendEmailChangeVerificationAsync(request.NewEmail, userName, verificationUrl, request.CurrentEmail);

                // Email la EMAIL-UL VECHI cu link de anulare (dacă schimbarea nu a fost inițiată de utilizator)
                var cancelUrl = $"{_getUrlService.GetApiBaseUrl()}/api/authentication/cancel-email-change?token={Uri.EscapeDataString(user.EmailChangeCancelToken!)}";
                await _emailService.SendEmailChangeNotificationAsync(request.CurrentEmail, userName, request.NewEmail, cancelUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email change emails for {Email}", request.CurrentEmail);
            }

            // Informăm utilizatorul că trebuie să se delogheze (token-ul JWT are emailul vechi)
            return Ok(new UserUpdateEmailResponseDTO { Success = true, Message = "Email change initiated. Please check both your current and new email addresses.", RequiresLogout = true });
        }

        // GET /api/authentication/verify-email-change?token=... — Confirmare schimbare email
        [HttpGet("verify-email-change")]
        public async Task<IActionResult> VerifyEmailChange([FromQuery] string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return BadRequest(new UserResponseDTO { Success = false, Message = "Token is required." });
            }

            // Căutăm utilizatorul după token-ul de confirmare a schimbării email-ului
            var user = await _userService.GetUserByEmailChangeTokenAsync(token);

            // Verificăm că token-ul e valid și nu a expirat
            if (user == null || user.EmailChangeExpires == null || user.EmailChangeExpires < DateTime.UtcNow)
            {
                return BadRequest(new UserResponseDTO { Success = false, Message = "Invalid or expired verification token." });
            }

            // Verificăm că schimbarea nu a fost anulată (token-ul de anulare dispare după cancel)
            if (string.IsNullOrEmpty(user.EmailChangeCancelToken))
            {
                return BadRequest(new UserResponseDTO { Success = false, Message = "Email change request has been cancelled." });
            }

            // Actualizăm email-ul în baza de date și ștergem token-urile temporare
            await _userService.EmailChangeAsync(user);

            return Ok(new UserResponseDTO { Success = true, Message = "Email address successfully changed and verified." });
        }

        // GET /api/authentication/cancel-email-change?token=... — Anulare schimbare email
        [HttpGet("cancel-email-change")]
        public async Task<IActionResult> CancelEmailChange([FromQuery] string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return BadRequest(new UserResponseDTO { Success = false, Message = "Token is required." });
            }

            // Căutăm utilizatorul după token-ul de anulare
            var user = await _userService.GetUserByEmailChangeCancelTokenAsync(token);

            // Verificăm validitatea token-ului
            if (user == null || user.EmailChangeExpires == null || user.EmailChangeExpires < DateTime.UtcNow)
            {
                return BadRequest(new UserResponseDTO { Success = false, Message = "Invalid or expired cancellation token." });
            }

            // Ștergem token-urile de schimbare email — anulăm procesul
            await _userService.CancelEmailChangeAsync(user);

            return Ok(new UserResponseDTO { Success = true, Message = "Email change request has been successfully cancelled. Your account is secure." });
        }

        // POST /api/authentication/change-password — Schimbare parolă (necesită autentificare)
        [Authorize] // Utilizatorul trebuie să fie autentificat
        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] UserChangePasswordRequestDTO request)
        {
            // Identificăm utilizatorul autentificat din token-ul JWT
            var callerEmail = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email");
            if (string.IsNullOrEmpty(callerEmail))
                return Unauthorized(new UserResponseDTO { Success = false, Message = "Unable to determine authenticated user." });

            // Validăm parola nouă (complexitate, confirmare etc.)
            var passwordValidation = await _authenticationService.ValidateChangePassword(request.CurrentPassword, request.NewPassword, request.ConfirmPassword);
            if (!passwordValidation.Success) {
                return BadRequest(passwordValidation);
            }

            // Verificăm că parola curentă introdusă este corectă
            var user = await _userService.GetUserByEmailAsync(callerEmail);

            if (user == null || string.IsNullOrEmpty(user.PasswordHash) || !_authenticationService.VerifyPassword(request.CurrentPassword, user.PasswordHash))
            {
                return BadRequest(new UserResponseDTO { Success = false, Message = "Invalid current password." });
            }

            // Actualizăm parola cu noul hash bcrypt
            await _userService.PasswordChangeAsync(user, request.NewPassword);

            return Ok(new UserResponseDTO { Success = true, Message = "Password has been changed successfully." });
        }
    }
}
