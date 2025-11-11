using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace LifeAlertPlus.Infrastructure.Context
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddLifeAlertPlusDbContext(this IServiceCollection services, string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("Connection string is required", nameof(connectionString));

            services.AddDbContext<LifeAlertPlusDbContext>(options =>
                options.UseSqlite(connectionString));

            return services;
        }
    }
}
