using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using LifeAlertPlus.API.Services;
using LifeAlertPlus.Application.IServices;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [AllowAnonymous]
    [Route("api/auth")]
    public class GoogleAuthController(
        IUserService userService,
        IJwtService jwtService,
        ILogger<GoogleAuthController> logger,
        GetUrlService getUrlService,
        IRoleService roleService) : ControllerBase
    {
        // One-time codes: code → (jwt, expiry). Cleaned up on exchange.
        private static readonly ConcurrentDictionary<string, (string Jwt, DateTime Expiry)> _pendingTokens = new();

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
                logger.LogWarning("Google authentication failed");
                return Redirect($"{getUrlService.GetClientBaseUrl()}/login?error=GoogleAuthFailed");
            }

            var email     = authenticateResult.Principal.FindFirst(c => c.Type == ClaimTypes.Email)?.Value;
            var name      = authenticateResult.Principal.Identity?.Name;
            var givenName = authenticateResult.Principal.FindFirst(c => c.Type == ClaimTypes.GivenName)?.Value;
            var familyName    = authenticateResult.Principal.FindFirst(c => c.Type == ClaimTypes.Surname)?.Value;
            var profilePictureUrl = authenticateResult.Principal.FindFirst(c => c.Type == "picture")?.Value
                ?? authenticateResult.Principal.FindFirst(c => c.Type == "urn:google:picture")?.Value;
            var googleId = authenticateResult.Principal.FindFirst(c => c.Type == ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(googleId))
            {
                logger.LogWarning("Google authentication missing email or googleId");
                return Redirect($"{getUrlService.GetClientBaseUrl()}/login?error=GoogleAuthNoEmail");
            }

            var user = await userService.FindOrCreateGoogleUserAsync(email, name, googleId, givenName, familyName, profilePictureUrl);
            if (user == null)
            {
                logger.LogError("Failed to find or create Google user for email {Email}", email);
                return Redirect($"{getUrlService.GetClientBaseUrl()}/login?error=GoogleUserCreateFailed");
            }

            var roleName = (await roleService.GetByIdAsync(user.RoleId))?.Name ?? "User";
            var jwt = jwtService.GenerateToken(user, roleName);

            // Store JWT as a short-lived one-time code instead of putting it in the URL fragment.
            // The client exchanges the code for the JWT via /api/auth/exchange-token.
            var code = Guid.NewGuid().ToString("N");
            _pendingTokens[code] = (jwt, DateTime.UtcNow.AddSeconds(60));

            // Forward returnUrl so the client can navigate to the right page after login.
            var safeReturn = string.IsNullOrWhiteSpace(returnUrl) ? "/dashboard" : returnUrl;
            return Redirect($"{getUrlService.GetClientBaseUrl()}/auth/callback?code={code}&returnUrl={Uri.EscapeDataString(safeReturn)}");
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

            return Ok(new { Token = entry.Jwt });
        }
    }
}
