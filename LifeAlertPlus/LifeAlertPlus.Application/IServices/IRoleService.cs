using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Application.IServices
{
    public interface IRoleService
    {
        Task<Role?> GetByNameAsync(string name);
        Task<Role?> GetByIdAsync(Guid id);
    }
}
