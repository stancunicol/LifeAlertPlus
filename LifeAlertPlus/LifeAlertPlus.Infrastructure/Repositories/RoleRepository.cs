using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace LifeAlertPlus.Infrastructure.Repositories
{
    public class RoleRepository : IRoleRepository
    {
        private readonly LifeAlertPlusDbContext _dbContext;

        public RoleRepository(LifeAlertPlusDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<Role?> GetRoleByNameAsync(string name)
        {
            return await _dbContext.Roles.FirstOrDefaultAsync(r => r.Name == name);
        }

        public async Task<Role?> GetRoleByIdAsync(Guid id)
        {
            return await _dbContext.Roles.FirstOrDefaultAsync(r => r.Id == id);
        }
    }
}
