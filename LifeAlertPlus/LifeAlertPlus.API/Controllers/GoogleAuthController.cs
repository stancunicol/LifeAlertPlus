using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Mvc;
using LifeAlertPlus.Application.IServices;
using System.Security.Claims;
using LifeAlertPlus.API.Services;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class GoogleAuthController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IJwtService _jwtService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GoogleAuthController> _logger;
        private readonly GetUrlService _getUrlService;

        public GoogleAuthController(IUserService userService, IJwtService jwtService, IConfiguration configuration, ILogger<GoogleAuthController> logger, GetUrlService getUrlService)
        {
            _userService = userService;
            _jwtService = jwtService;
            _configuration = configuration;
            _logger = logger;
            _getUrlService = getUrlService;
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

            var jwt = _jwtService.GenerateToken(user);

            return Redirect($"{_getUrlService.GetClientBaseUrl()}{returnUrl}#token={jwt}");
        }
    }
}
