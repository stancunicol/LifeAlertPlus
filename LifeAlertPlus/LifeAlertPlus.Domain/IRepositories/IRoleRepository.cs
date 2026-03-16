using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Domain.IRepositories
{
    public interface IRoleRepository
    {
        Task<Role?> GetRoleByNameAsync(string name);
        Task<Role?> GetRoleByIdAsync(Guid id);
    }
}
