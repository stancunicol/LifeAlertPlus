using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    public abstract class BaseApiController : ControllerBase
    {
        protected Guid? GetCallerId()
        {
            var idStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value
                ?? User.FindFirst("nameid")?.Value;
            return idStr != null && Guid.TryParse(idStr, out var id) ? id : null;
        }

        protected string GetCallerRole()
        {
            return User.FindFirst(ClaimTypes.Role)?.Value
                ?? User.FindFirst("role")?.Value
                ?? string.Empty;
        }

        protected bool IsAdminRole() => IsAdminRole(GetCallerRole());

        protected static bool IsAdminRole(string? role)
        {
            return !string.IsNullOrWhiteSpace(role)
                && role.IndexOf("admin", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
