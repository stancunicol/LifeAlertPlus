using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace LifeAlertPlus.Infrastructure.Context
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddLifeAlertPlusDbContext(this IServiceCollection services, string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("Connection string is required", nameof(connectionString));

            // When running locally, resolve DB path to the Infrastructure project folder.
            // On Azure/production, if a relative filename like "lifealert.db" is given
            // and the Infrastructure folder doesn't exist, place it in the app's content root.
            if (connectionString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase))
            {
                var dbFileName = connectionString.Replace("Data Source=", "", StringComparison.OrdinalIgnoreCase).Trim();
                if (!Path.IsPathRooted(dbFileName) && !dbFileName.Contains(Path.DirectorySeparatorChar) && !dbFileName.Contains('/'))
                {
                    // Try Infrastructure folder first (local dev)
                    var infraDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LifeAlertPlus.Infrastructure"));
                    if (!Directory.Exists(infraDir))
                        infraDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "LifeAlertPlus.Infrastructure"));

                    if (Directory.Exists(infraDir))
                    {
                        connectionString = $"Data Source={Path.Combine(infraDir, dbFileName)}";
                    }
                    else
                    {
                        // Azure App Service: WEBSITE_SITE_NAME is always set on App Service.
                        // Use /home/data/ — the only path that is both writable and persistent
                        // regardless of WEBSITE_RUN_FROM_PACKAGE mode.
                        var siteName = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME");
                        string baseDir;
                        if (!string.IsNullOrEmpty(siteName))
                        {
                            var home = Environment.GetEnvironmentVariable("HOME") ?? "/home";
                            baseDir = Path.Combine(home, "data");
                        }
                        else
                        {
                            baseDir = Directory.GetCurrentDirectory();
                        }
                        Directory.CreateDirectory(baseDir);
                        connectionString = $"Data Source={Path.Combine(baseDir, dbFileName)}";
                    }
                }
            }

            services.AddDbContext<LifeAlertPlusDbContext>(options =>
                options.UseSqlite(connectionString));

            return services;
        }
    }
}
