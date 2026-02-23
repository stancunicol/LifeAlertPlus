using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LifeAlertPlus.Application.IServices;
using System.Security.Claims;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class GoogleAuthController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IJwtService _jwtService;
        private readonly IConfiguration _configuration;

        public GoogleAuthController(IUserService userService, IJwtService jwtService, IConfiguration configuration)
        {
            _userService = userService;
            _jwtService = jwtService;
            _configuration = configuration;
        }

        private string GetClientBaseUrl()
        {
            var configured = _configuration["Urls:ClientBaseUrl"];
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured.TrimEnd('/');
            }

            return $"{Request.Scheme}://{Request.Host}";
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
            var authenticateResult = await HttpContext.AuthenticateAsync(GoogleDefaults.AuthenticationScheme);
            if (!authenticateResult.Succeeded || authenticateResult.Principal == null)
            {
                return Redirect($"{GetClientBaseUrl()}/login?error=GoogleAuthFailed");
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
                return Redirect($"{GetClientBaseUrl()}/login?error=GoogleAuthNoEmail");
            }

            var user = await _userService.FindOrCreateGoogleUserAsync(email, name, googleId, givenName, familyName, profilePictureUrl);
            if (user == null)
            {
                return Redirect($"{GetClientBaseUrl()}/login?error=GoogleUserCreateFailed");
            }

            var jwt = _jwtService.GenerateToken(user);

            return Redirect($"{returnUrl}?token={jwt}");
        }
    }
}
