using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Domain.IRepositories
{
    // Interfață repository pentru tabela Roles (Admin, User etc.)
    public interface IRoleRepository
    {
        Task<Role?> GetRoleByNameAsync(string name); // Caută rolul după nume (ex: "Admin", "User") — folosit la înregistrare
        Task<Role?> GetRoleByIdAsync(Guid id);       // Caută rolul după ID — folosit când avem ID-ul din claims JWT
    }
}
