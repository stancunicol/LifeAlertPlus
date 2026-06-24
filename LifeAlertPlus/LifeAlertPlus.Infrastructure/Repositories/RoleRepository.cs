using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace LifeAlertPlus.Infrastructure.Repositories
{
    // Implementare EF Core a IRoleRepository — acces direct la tabela Roles din PostgreSQL
    public class RoleRepository : IRoleRepository
    {
        private readonly LifeAlertPlusDbContext _dbContext;

        public RoleRepository(LifeAlertPlusDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        // SELECT după nume exact (ex: "Admin", "User") — folosit la crearea conturilor noi
        public async Task<Role?> GetRoleByNameAsync(string name)
        {
            return await _dbContext.Roles.FirstOrDefaultAsync(r => r.Name == name);
        }

        // SELECT după ID — folosit când avem doar RoleId (din claims JWT sau entitatea User)
        public async Task<Role?> GetRoleByIdAsync(Guid id)
        {
            return await _dbContext.Roles.FirstOrDefaultAsync(r => r.Id == id);
        }
    }
}
