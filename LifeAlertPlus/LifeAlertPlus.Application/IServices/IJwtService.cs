using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Application.IServices
{
    // Interfață pentru serviciul de generare token-uri JWT
    public interface IJwtService
    {
        string GenerateToken(User user, string roleName); // Generează un token JWT semnat (HS256) cu claims: userId, email, rol, expiră în ExpiresInMinutes
    }
}
