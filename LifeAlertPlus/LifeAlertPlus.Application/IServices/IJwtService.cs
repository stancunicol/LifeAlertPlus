using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Application.IServices
{
    public interface IJwtService
    {
        string GenerateToken(User user);
    }
}
