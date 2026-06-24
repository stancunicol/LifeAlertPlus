using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Application.IServices
{
    // Interfață pentru serviciul de gestionare roluri (Admin, User etc.)
    public interface IRoleService
    {
        Task<Role?> GetByNameAsync(string name); // Caută un rol după nume (ex: "Admin", "User")
        Task<Role?> GetByIdAsync(Guid id);       // Caută un rol după ID (GUID)
    }
}
