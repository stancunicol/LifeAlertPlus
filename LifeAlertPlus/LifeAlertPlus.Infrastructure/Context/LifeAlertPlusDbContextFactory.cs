using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LifeAlertPlus.Infrastructure.Context
{
    public class LifeAlertPlusDbContextFactory : IDesignTimeDbContextFactory<LifeAlertPlusDbContext>
    {
        public LifeAlertPlusDbContext CreateDbContext(string[] args)
        {
            var baseDir = Directory.GetCurrentDirectory();

            var infraPath = Path.GetFullPath(Path.Combine(baseDir));
            var dbPath = Path.Combine(infraPath, "lifealert.db");

            var connectionString = $"Data Source={dbPath}";

            var optionsBuilder = new DbContextOptionsBuilder<LifeAlertPlusDbContext>();
            optionsBuilder.UseSqlite(connectionString);

            return new LifeAlertPlusDbContext(optionsBuilder.Options);
        }
    }
}
