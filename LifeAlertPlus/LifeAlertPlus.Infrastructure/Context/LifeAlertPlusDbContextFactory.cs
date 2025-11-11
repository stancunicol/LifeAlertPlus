using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LifeAlertPlus.Infrastructure.Context
{
    // This factory is used by the EF tools at design-time to create the DbContext
    public class LifeAlertPlusDbContextFactory : IDesignTimeDbContextFactory<LifeAlertPlusDbContext>
    {
        public LifeAlertPlusDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<LifeAlertPlusDbContext>();

            // default to local sqlite file; tools may pass a connection string as first arg
            var connectionString = "Data Source=lifealert.db";
            if (args != null && args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
            {
                connectionString = args[0];
            }

            optionsBuilder.UseSqlite(connectionString);
            return new LifeAlertPlusDbContext(optionsBuilder.Options);
        }
    }
}
