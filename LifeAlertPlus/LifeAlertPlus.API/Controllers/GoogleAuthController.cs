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
    // Controller pentru fluxul de autentificare OAuth 2.0 cu Google.
    // Fluxul complet:
    // 1. Utilizatorul apasă "Login cu Google" în Blazor
    // 2. GET /api/auth/google-login → redirect la Google OAuth consent screen
    // 3. Google redirectează înapoi la GET /api/auth/google-response cu codul de autorizare
    // 4. ASP.NET Core face schimbul de cod pentru token (prin cookie de correlație)
    // 5. Controller-ul generează JWT și îl stochează ca one-time-code (OTC) de 60 secunde
    // 6. Blazor primește OTC-ul și îl schimbă pe JWT via GET /api/auth/exchange-token
    [ApiController]
    [AllowAnonymous] // Google OAuth nu necesită JWT propriu (e fluxul de obținere a lui)
    [Route("api/auth")]
    public class GoogleAuthController(
        IUserService userService,
        IJwtService jwtService,
        ILogger<GoogleAuthController> logger,
        GetUrlService getUrlService,
        IRoleService roleService) : ControllerBase
    {
        // Dicționar thread-safe pentru coduri unice de schimb: code → (JWT, expiry)
        // Curățat la fiecare apel ExchangeToken (curățare oportunistă, nu background job)
        private static readonly ConcurrentDictionary<string, (string Jwt, DateTime Expiry)> _pendingTokens = new();

        // GET /api/auth/google-login?returnUrl=... — Inițiază fluxul OAuth cu Google
        // Returnează un redirect HTTP 302 către pagina de consimțământ Google
        [HttpGet("google-login")]
        public IActionResult GoogleLogin(string? returnUrl = "/")
        {
            // Construim URL-ul de callback unde Google va redirecta după autentificare
            var redirectUrl = Url.Action(nameof(GoogleResponse), "GoogleAuth", new { returnUrl });
            var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
            // Challenge forțează ASP.NET Core să inițieze fluxul OAuth Google
            return Challenge(properties, GoogleDefaults.AuthenticationScheme);
        }

        // GET /api/auth/google-response?returnUrl=... — Callback-ul după autentificarea Google
        // Apelat automat de Google cu cookie-ul de correlație după consimțământul utilizatorului
        [HttpGet("google-response")]
        public async Task<IActionResult> GoogleResponse(string? returnUrl = "/")
        {
            if (!string.IsNullOrEmpty(returnUrl))
            {
                // Dacă clientul a trimis un URL absolut (ex. http://localhost:5254/dashboard),
                // extragem doar path-ul pentru a evita dublarea prefixului în redirect-ul final
                if (Uri.TryCreate(returnUrl, UriKind.Absolute, out var parsedUri))
                    returnUrl = parsedUri.PathAndQuery;
                else if (!Url.IsLocalUrl(returnUrl))
                    returnUrl = "/"; // Prevenim open redirect attacks
            }

            // Validăm cookie-ul de correlație creat de fluxul OAuth
            var authenticateResult = await HttpContext.AuthenticateAsync("Cookies");
            if (!authenticateResult.Succeeded || authenticateResult.Principal == null)
            {
                // Logăm detalii suficiente pentru a distinge modurile de eșec comune:
                // cookie lipsă, SameSite drop, correlație expirată etc.
                logger.LogWarning(
                    "Google authentication failed at /google-response. Succeeded={Succeeded}, Failure={Failure}, " +
                    "HasPrincipal={HasPrincipal}, RequestScheme={Scheme}, RequestHost={Host}, HasCookieHeader={HasCookie}",
                    authenticateResult.Succeeded,
                    authenticateResult.Failure?.Message,
                    authenticateResult.Principal != null,
                    Request.Scheme,
                    Request.Host.Value,
                    Request.Headers.ContainsKey("Cookie"));
                return Redirect($"{getUrlService.GetClientBaseUrl()}/login?error=GoogleAuthFailed");
            }

            // Extragem datele profilului Google din claims-urile tokenului
            var email             = authenticateResult.Principal.FindFirst(c => c.Type == ClaimTypes.Email)?.Value;
            var name              = authenticateResult.Principal.Identity?.Name;
            var givenName         = authenticateResult.Principal.FindFirst(c => c.Type == ClaimTypes.GivenName)?.Value;
            var familyName        = authenticateResult.Principal.FindFirst(c => c.Type == ClaimTypes.Surname)?.Value;
            var profilePictureUrl = authenticateResult.Principal.FindFirst(c => c.Type == "picture")?.Value
                ?? authenticateResult.Principal.FindFirst(c => c.Type == "urn:google:picture")?.Value; // Fallback claim
            var googleId          = authenticateResult.Principal.FindFirst(c => c.Type == ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(googleId))
            {
                logger.LogWarning("Google authentication missing email or googleId");
                return Redirect($"{getUrlService.GetClientBaseUrl()}/login?error=GoogleAuthNoEmail");
            }

            Domain.Entities.User? user;
            try
            {
                // Găsim sau creăm utilizatorul în DB: dacă există deja cu GoogleId, îl actualizăm;
                // dacă e nou, îl creăm cu Provider="Google"
                user = await userService.FindOrCreateGoogleUserAsync(email, name, googleId, givenName, familyName, profilePictureUrl);
            }
            catch (Application.Services.GoogleEmailConflictException)
            {
                // Contul clasic (email+parolă) există deja cu acest email — refuzăm fuziunea
                // și îi cerem utilizatorului să se logheze cu parola lui
                logger.LogWarning("Google sign-in blocked: email {Email} is already registered with a different provider.", email);
                return Redirect($"{getUrlService.GetClientBaseUrl()}/login?error=GoogleEmailConflict");
            }

            if (user == null)
            {
                logger.LogError("Failed to find or create Google user for email {Email}", email);
                return Redirect($"{getUrlService.GetClientBaseUrl()}/login?error=GoogleUserCreateFailed");
            }

            // Generăm JWT-ul nostru propriu cu claims-urile utilizatorului
            var roleName = (await roleService.GetByIdAsync(user.RoleId))?.Name ?? "User";
            var jwt = jwtService.GenerateToken(user, roleName);

            // Stocăm JWT-ul ca one-time-code (OTC) valid 60 de secunde
            // Nu punem JWT-ul direct în URL-ul de redirect (ar apărea în logs/history)
            // Clientul Blazor va schimba OTC-ul pe JWT via /api/auth/exchange-token
            var code = Guid.NewGuid().ToString("N"); // 32 caractere hexazecimale aleatorii
            _pendingTokens[code] = (jwt, DateTime.UtcNow.AddSeconds(60));

            // Redirecționăm Blazor la /auth/callback cu codul și pagina de destinație
            var safeReturn = string.IsNullOrWhiteSpace(returnUrl) ? "/dashboard" : returnUrl;
            return Redirect($"{getUrlService.GetClientBaseUrl()}/auth/callback?code={code}&returnUrl={Uri.EscapeDataString(safeReturn)}");
        }

        // GET /api/auth/exchange-token?code=... — Schimbă OTC-ul pe JWT real
        // Blazor apelează acest endpoint la /auth/callback după ce primește codul din URL
        [HttpGet("exchange-token")]
        public IActionResult ExchangeToken([FromQuery] string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return BadRequest(new { Message = "Code is required." });

            // Curățăm codurile expirate (pentru a nu acumula memorie)
            var expired = _pendingTokens.Where(kv => kv.Value.Expiry < DateTime.UtcNow).Select(kv => kv.Key).ToList();
            foreach (var key in expired) _pendingTokens.TryRemove(key, out _);

            // TryRemove este atomic — prevenim reutilizarea aceluiași cod (one-time use)
            if (!_pendingTokens.TryRemove(code, out var entry) || entry.Expiry < DateTime.UtcNow)
                return BadRequest(new { Message = "Invalid or expired code." });

            return Ok(new { Token = entry.Jwt }); // Blazor stochează JWT-ul în localStorage
        }
    }
}
