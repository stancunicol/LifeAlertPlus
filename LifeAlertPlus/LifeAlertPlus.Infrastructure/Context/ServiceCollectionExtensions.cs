using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace LifeAlertPlus.Infrastructure.Context
{
    // Metodă de extensie pentru înregistrarea DbContext-ului în containerul DI (apelată din Program.cs)
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddLifeAlertPlusDbContext(this IServiceCollection services, string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Connection string is required", nameof(connectionString));

            // Npgsql = provider-ul EF Core pentru PostgreSQL (Azure Database for PostgreSQL Flexible Server în producție)
            services.AddDbContext<LifeAlertPlusDbContext>(options =>
                options.UseNpgsql(connectionString));

            return services;
        }
    }
}
