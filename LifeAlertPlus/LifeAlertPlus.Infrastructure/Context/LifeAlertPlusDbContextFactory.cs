using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LifeAlertPlus.Infrastructure.Context
{
    // This factory is used by the EF tools at design-time to create the DbContext
    public class LifeAlertPlusDbContextFactory : IDesignTimeDbContextFactory<LifeAlertPlusDbContext>
    {
        public LifeAlertPlusDbContext CreateDbContext(string[] args)
        {
            var baseDir = Directory.GetCurrentDirectory();

            // când rulezi "dotnet ef", currentDirectory este proiectul Infrastructure
            var infraPath = Path.GetFullPath(Path.Combine(baseDir));
            var dbPath = Path.Combine(infraPath, "lifealert.db");

            var connectionString = $"Data Source={dbPath}";

            var optionsBuilder = new DbContextOptionsBuilder<LifeAlertPlusDbContext>();
            optionsBuilder.UseSqlite(connectionString);

            return new LifeAlertPlusDbContext(optionsBuilder.Options);
        }
    }
}
