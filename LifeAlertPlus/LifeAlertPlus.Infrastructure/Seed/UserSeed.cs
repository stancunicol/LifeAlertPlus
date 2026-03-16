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

            var roles = new List<Role>
            {
                new Role { Id = Guid.NewGuid(), Name = "Admin", CreatedAt = DateTime.UtcNow },
                new Role { Id = Guid.NewGuid(), Name = "User", CreatedAt = DateTime.UtcNow }
            };

            foreach (var role in roles)
            {
                if (!await context.Roles.AnyAsync(r => r.Name == role.Name))
                {
                    context.Roles.Add(role);
                }
            }
            await context.SaveChangesAsync();

            var adminEmail = "admin@gmail.com";

            if (!await context.Users.AnyAsync(u => u.Email == adminEmail))
            {
                var admin = new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Admin",
                    LastName = "User",
                    Email = adminEmail,
                    RoleId = roles.First(r => r.Name == "Admin").Id,
                    IsEmailConfirmed = true,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin123!"),
                    Provider = "Local",
                    CreatedAt = DateTime.UtcNow
                };

                var user = new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Nicol",
                    LastName = "Stancu",
                    Email = "stancunicol3@gmail.com",
                    RoleId = roles.First(r => r.Name == "User").Id,
                    IsEmailConfirmed = true,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("20042005!Nicol"),
                    Provider = "Local",
                    CreatedAt = DateTime.UtcNow
                };

                context.Users.Add(admin);
                context.Users.Add(user);

                await context.SaveChangesAsync();
            }
        }
    }
}