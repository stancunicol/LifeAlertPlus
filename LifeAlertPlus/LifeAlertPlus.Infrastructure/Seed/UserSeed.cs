using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LifeAlertPlus.Infrastructure.Seed
{
    public static class UserSeed
    {
        public static async Task SeedAsync(IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<LifeAlertPlusDbContext>();

            await context.Database.MigrateAsync();

            var hasUsers = await context.Users.AnyAsync();
            if (!hasUsers)
            {
                var profileImagesFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "profile-images");
                if (Directory.Exists(profileImagesFolder))
                {
                    foreach (var file in Directory.GetFiles(profileImagesFolder))
                    {
                        File.Delete(file);
                    }
                }
            }

            var adminEmail = "admin@gmail.com";

            if (!await context.Users.AnyAsync(u => u.Email == adminEmail))
            {
                var admin = new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Nicol",
                    LastName = "Stancu",
                    Email = adminEmail,
                    IsEmailConfirmed = true,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("20042005!Nicol"),
                    Provider = "Local",
                    CreatedAt = DateTime.UtcNow
                };

                context.Users.Add(admin);
                await context.SaveChangesAsync();
            }
        }
    }
}