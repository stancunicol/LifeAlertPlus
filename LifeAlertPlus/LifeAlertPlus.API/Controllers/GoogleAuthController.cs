using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LifeAlertPlus.Application.IServices;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class GoogleAuthController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IJwtService _jwtService;

        public GoogleAuthController(IUserService userService, IJwtService jwtService)
        {
            _userService = userService;
            _jwtService = jwtService;
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
                return Redirect($"/login?error=GoogleAuthFailed");
            }

            var email = authenticateResult.Principal.FindFirst(c => c.Type == System.Security.Claims.ClaimTypes.Email)?.Value;
            var name = authenticateResult.Principal.Identity?.Name;
            var googleId = authenticateResult.Principal.FindFirst(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(googleId))
            {
                return Redirect($"/login?error=GoogleAuthNoEmail");
            }

            var user = await _userService.FindOrCreateGoogleUserAsync(email, name, googleId);
            if (user == null)
            {
                return Redirect($"/login?error=GoogleUserCreateFailed");
            }

            var jwt = _jwtService.GenerateToken(user);

            return Redirect($"{returnUrl}?token={jwt}");
        }
    }
}
