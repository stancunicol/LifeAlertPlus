using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;

namespace LifeAlertPlus.Application.Services
{
    // Serviciu pentru gestionarea rolurilor — delegă complet către IRoleRepository
    public class RoleService : IRoleService
    {
        private readonly IRoleRepository _roleRepository; // Acces la tabela Roles din DB

        public RoleService(IRoleRepository roleRepository)
        {
            _roleRepository = roleRepository;
        }

        // Caută un rol după nume (ex: "Admin", "User") — folosit la înregistrare și autentificare
        public Task<Role?> GetByNameAsync(string name)
        {
            return _roleRepository.GetRoleByNameAsync(name);
        }

        // Caută un rol după ID — folosit când avem deja ID-ul din claims JWT
        public Task<Role?> GetByIdAsync(Guid id)
        {
            return _roleRepository.GetRoleByIdAsync(id);
        }
    }
}
