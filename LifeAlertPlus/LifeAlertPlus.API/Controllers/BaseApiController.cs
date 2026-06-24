using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace LifeAlertPlus.API.Controllers
{
    // Clasa de bază pentru toate controller-ele API — conține metode utilitare comune
    // Marcată ca abstract: nu poate fi instanțiată direct, doar moștenită
    [ApiController]
    public abstract class BaseApiController : ControllerBase
    {
        // Returnează ID-ul utilizatorului autentificat extras din token-ul JWT
        // Returnează null dacă nu există token sau ID-ul nu poate fi parsat ca Guid
        protected Guid? GetCallerId()
        {
            // Căutăm claim-ul cu ID-ul utilizatorului în mai multe formate posibile
            // (diferite biblioteci JWT pot folosi nume diferite pentru același claim)
            var idStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value // Format standard .NET
                ?? User.FindFirst("sub")?.Value  // Format standard OAuth 2.0 / OpenID Connect
                ?? User.FindFirst("nameid")?.Value; // Format alternativ JWT
            // Convertim string-ul la Guid — dacă eșuează, returnăm null
            return idStr != null && Guid.TryParse(idStr, out var id) ? id : null;
        }

        // Returnează rolul utilizatorului autentificat (ex: "Admin", "User")
        // Returnează string gol dacă claim-ul de rol lipsește
        protected string GetCallerRole()
        {
            return User.FindFirst(ClaimTypes.Role)?.Value // Format standard .NET
                ?? User.FindFirst("role")?.Value // Format alternativ JWT
                ?? string.Empty;
        }

        // Verifică dacă utilizatorul curent este administrator (metodă de instanță)
        protected bool IsAdminRole() => IsAdminRole(GetCallerRole());

        // Verifică dacă un rol dat conține cuvântul "admin" (căutare case-insensitive)
        // Metodă statică — poate fi apelată fără instanță și pe un rol primit ca parametru
        protected static bool IsAdminRole(string? role)
        {
            return !string.IsNullOrWhiteSpace(role)
                && role.IndexOf("admin", StringComparison.OrdinalIgnoreCase) >= 0; // "Admin", "admin", "ADMIN" — toate sunt acceptate
        }
    }
}
