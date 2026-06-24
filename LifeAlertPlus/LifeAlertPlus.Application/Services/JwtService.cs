using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace LifeAlertPlus.Application.Services
{
    // Serviciu de generare token-uri JWT (JSON Web Token) pentru autentificarea utilizatorilor
    // Algoritmul folosit: HS256 (HMAC-SHA256) — semnătură simetrică cu cheia din Jwt:Key
    public class JwtService : IJwtService
    {
        private readonly IConfiguration _config;
        private static readonly JwtSecurityTokenHandler _tokenHandler = new(); // Static: instanțierea e costisitoare, refolosim

        public JwtService(IConfiguration config)
        {
            _config = config;
        }

        // Generează un token JWT semnat pentru utilizatorul autentificat
        // Token-ul conține claims cu informații despre utilizator, accesibile în frontend fără apel DB
        public string GenerateToken(User user, string roleName)
        {
            var jwtKey = _config["Jwt:Key"];
            if (string.IsNullOrWhiteSpace(jwtKey))
                throw new InvalidOperationException("Missing Jwt:Key configuration.");

            if (Encoding.UTF8.GetByteCount(jwtKey) < 32) // HS256 necesită minim 256 biți (32 bytes)
                throw new InvalidOperationException("Jwt:Key must be at least 32 bytes for HS256.");

            var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)); // Cheia de semnare
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256); // Algoritm HS256

            // Claims incluse în token — accesibile direct în Blazor (fără apel API suplimentar)
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),    // ID utilizator (standard JWT "sub")
                new Claim(JwtRegisteredClaimNames.Email, user.Email),          // Email (standard JWT)
                new Claim("firstName", user.FirstName),                        // Prenume (custom claim)
                new Claim("lastName", user.LastName),                          // Nume (custom claim)
                new Claim("provider", user.Provider ?? "Local"),               // "Local" sau "Google" (metodă de autentificare)
                new Claim(ClaimTypes.Role, roleName),                          // Rol pentru [Authorize(Roles="Admin")] în API
                new Claim("role", roleName),                                   // Duplicat pentru compatibilitate Blazor (AuthenticationState)
                new Claim("lastChangedPasswordAt", user.LastChangedPasswordAt?.ToString("o") ?? string.Empty), // ISO 8601 — detectare token-uri emise înainte de schimbarea parolei
                new Claim("profilePictureUrl", user.ProfilePictureUrl ?? string.Empty), // URL avatar (afișat în navbar)
                // GDPR: dacă utilizatorul nu a acceptat consimțământul de procesare date,
                // frontend-ul îl redirecționează la /consent înainte de a folosi aplicația
                new Claim("needsConsent", user.DataProcessingConsentAt == null ? "true" : "false")
            };

            int.TryParse(_config["Jwt:ExpiresInMinutes"], out var expiresInMinutes);
            if (expiresInMinutes <= 0) expiresInMinutes = 60; // Fallback la 60 minute dacă nu e configurat

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],           // "LifeAlertPlus" — validat la fiecare cerere
                audience: _config["Jwt:Audience"],       // "LifeAlertPlusUsers" — validat la fiecare cerere
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(expiresInMinutes), // Data expirării token-ului
                signingCredentials: creds
            );

            return _tokenHandler.WriteToken(token); // Serializare token în format compact: header.payload.signature
        }
    }
}
