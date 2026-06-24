using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace LifeAlertPlus.Infrastructure.Context
{
    // Folosit de `dotnet ef migrations add` / `database update` la design-time (generarea migrărilor).
    // Citește același connection string ca runtime-ul, ca să existe o singură sursă de adevăr pentru credențiale.
    public class LifeAlertPlusDbContextFactory : IDesignTimeDbContextFactory<LifeAlertPlusDbContext>
    {
        public LifeAlertPlusDbContext CreateDbContext(string[] args)
        {
            // 1. Variabila de mediu explicită are prioritate (utilă în CI/CD).
            var connectionString = Environment.GetEnvironmentVariable("LIFEALERT_CONNECTION_STRING");

            // 2. Altfel citim ConnectionStrings:Default din appsettings-ul proiectului API.
            //    Unealta `dotnet ef` rulează de obicei din rădăcina soluției sau din folderul Infrastructure,
            //    deci încercăm ambele căi relative posibile.
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

            // 3. Fallback de ultimă instanță, ca `dotnet ef migrations add` (care are nevoie doar de
            //    providerul DB, nu de o conexiune reală) să funcționeze din orice locație.
            if (string.IsNullOrWhiteSpace(connectionString))
                connectionString = "Host=localhost;Port=5432;Database=lifealertplus;Username=postgres;Password=postgres";

            var optionsBuilder = new DbContextOptionsBuilder<LifeAlertPlusDbContext>();
            optionsBuilder.UseNpgsql(connectionString); // Provider PostgreSQL (Npgsql)

            return new LifeAlertPlusDbContext(optionsBuilder.Options);
        }
    }
}
