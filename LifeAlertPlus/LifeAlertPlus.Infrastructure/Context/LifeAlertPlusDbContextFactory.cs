using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace LifeAlertPlus.Infrastructure.Context
{
    // Used by `dotnet ef migrations add` / `database update` at design time.
    // Reads the same connection string the runtime uses, so credentials stay in one place.
    public class LifeAlertPlusDbContextFactory : IDesignTimeDbContextFactory<LifeAlertPlusDbContext>
    {
        public LifeAlertPlusDbContext CreateDbContext(string[] args)
        {
            // 1. Explicit env var override wins (useful in CI).
            var connectionString = Environment.GetEnvironmentVariable("LIFEALERT_CONNECTION_STRING");

            // 2. Otherwise read ConnectionStrings:Default from the API project's appsettings.
            //    The tool is typically run from the solution root or the Infrastructure folder,
            //    so try both relative locations.
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                var candidates = new[]
                {
                    Path.Combine(Directory.GetCurrentDirectory(), "LifeAlertPlus.API", "appsettings.json"),
                    Path.Combine(Directory.GetCurrentDirectory(), "..", "LifeAlertPlus.API", "appsettings.json"),
                };
                foreach (var path in candidates)
                {
                    if (File.Exists(path))
                    {
                        var cfg = new ConfigurationBuilder()
                            .AddJsonFile(path, optional: false)
                            .Build();
                        connectionString = cfg.GetConnectionString("Default");
                        if (!string.IsNullOrWhiteSpace(connectionString)) break;
                    }
                }
            }

            // 3. Last-resort fallback so `dotnet ef migrations add` (which only needs the
            //    provider, not a real DB) still works from arbitrary locations.
            if (string.IsNullOrWhiteSpace(connectionString))
                connectionString = "Host=localhost;Port=5432;Database=lifealertplus;Username=postgres;Password=postgres";

            var optionsBuilder = new DbContextOptionsBuilder<LifeAlertPlusDbContext>();
            optionsBuilder.UseNpgsql(connectionString);

            return new LifeAlertPlusDbContext(optionsBuilder.Options);
        }
    }
}
