using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using LifeAlertPlus.API.Services;
using LifeAlertPlus.Application.IServices;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class GoogleAuthController : ControllerBase
    {
        // One-time codes: code → (jwt, expiry). Cleaned up on exchange.
        private static readonly ConcurrentDictionary<string, (string Jwt, DateTime Expiry)> _pendingTokens = new();

        private readonly IUserService _userService;
        private readonly IJwtService _jwtService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GoogleAuthController> _logger;
        private readonly GetUrlService _getUrlService;
        private readonly IRoleService _roleService;

        public GoogleAuthController(IUserService userService, IJwtService jwtService, IConfiguration configuration, ILogger<GoogleAuthController> logger, GetUrlService getUrlService, IRoleService roleService)
        {
            _userService = userService;
            _jwtService = jwtService;
            _configuration = configuration;
            _logger = logger;
            _getUrlService = getUrlService;
            _roleService = roleService;
        }

        [HttpGet("google-login")]
        public IActionResult GoogleLogin(string? returnUrl = "/")
        {
            var redirectUrl = Url.Action(nameof(GoogleResponse), "GoogleAuth", new { returnUrl });
            var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
            return Challenge(properties, GoogleDefaults.AuthenticationScheme);
        }

        [HttpGet("google-response")]
        public async Task<IActionResult> GoogleResponse(string? returnUrl = "/")
        {
            if (!string.IsNullOrEmpty(returnUrl))
            {
                // If the client sent an absolute URL (e.g. http://localhost:5254/dashboard),
                // extract only the path to prevent a double-prefix in the final redirect.
                if (Uri.TryCreate(returnUrl, UriKind.Absolute, out var parsedUri))
                    returnUrl = parsedUri.PathAndQuery;
                else if (!Url.IsLocalUrl(returnUrl))
                    returnUrl = "/";
            }

            var authenticateResult = await HttpContext.AuthenticateAsync("Cookies");
            if (!authenticateResult.Succeeded || authenticateResult.Principal == null)
            {
                _logger.LogWarning("Google authentication failed");
                return Redirect($"{_getUrlService.GetClientBaseUrl()}/login?error=GoogleAuthFailed");
            }

            var email = authenticateResult.Principal.FindFirst(c => c.Type == ClaimTypes.Email)?.Value;
            var name = authenticateResult.Principal.Identity?.Name;
            var givenName = authenticateResult.Principal.FindFirst(c => c.Type == ClaimTypes.GivenName)?.Value;
            var familyName = authenticateResult.Principal.FindFirst(c => c.Type == ClaimTypes.Surname)?.Value;
            var profilePictureUrl = authenticateResult.Principal.FindFirst(c => c.Type == "picture")?.Value
                ?? authenticateResult.Principal.FindFirst(c => c.Type == "urn:google:picture")?.Value;
            var googleId = authenticateResult.Principal.FindFirst(c => c.Type == ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(googleId))
            {
                _logger.LogWarning("Google authentication missing email or googleId");
                return Redirect($"{_getUrlService.GetClientBaseUrl()}/login?error=GoogleAuthNoEmail");
            }

            var user = await _userService.FindOrCreateGoogleUserAsync(email, name, googleId, givenName, familyName, profilePictureUrl);
            if (user == null)
            {
                _logger.LogError("Failed to find or create Google user for email {Email}", email);
                return Redirect($"{_getUrlService.GetClientBaseUrl()}/login?error=GoogleUserCreateFailed");
            }

            var roleName = (await _roleService.GetByIdAsync(user.RoleId))?.Name ?? "User";
            var jwt = _jwtService.GenerateToken(user, roleName);

            // Store JWT as a short-lived one-time code instead of putting it in the URL fragment.
            // The client exchanges the code for the JWT via /api/auth/exchange-token.
            var code = Guid.NewGuid().ToString("N");
            _pendingTokens[code] = (jwt, DateTime.UtcNow.AddSeconds(60));

            return Redirect($"{_getUrlService.GetClientBaseUrl()}/auth/callback?code={code}");
        }

        [HttpGet("exchange-token")]
        public IActionResult ExchangeToken([FromQuery] string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return BadRequest(new { Message = "Code is required." });

            // Purge expired codes opportunistically
            var expired = _pendingTokens.Where(kv => kv.Value.Expiry < DateTime.UtcNow).Select(kv => kv.Key).ToList();
            foreach (var key in expired) _pendingTokens.TryRemove(key, out _);

            if (!_pendingTokens.TryRemove(code, out var entry) || entry.Expiry < DateTime.UtcNow)
                return BadRequest(new { Message = "Invalid or expired code." });

            return Ok(new { token = entry.Jwt });
        }
    }
}
