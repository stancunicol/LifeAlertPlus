using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;

namespace LifeAlertPlus.Application.Services
{
    public class RoleService : IRoleService
    {
        private readonly IRoleRepository _roleRepository;

        public RoleService(IRoleRepository roleRepository)
        {
            _roleRepository = roleRepository;
        }

        public Task<Role?> GetByNameAsync(string name)
        {
            return _roleRepository.GetRoleByNameAsync(name);
        }

        public Task<Role?> GetByIdAsync(Guid id)
        {
            return _roleRepository.GetRoleByIdAsync(id);
        }
    }
}
