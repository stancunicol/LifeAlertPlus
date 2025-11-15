using System;
using System.Linq;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Infrastructure.Context;
using BCrypt.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LifeAlertPlus.Infrastructure.Seed
{
    public static class UserSeed
    {
        public static void Seed(IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<LifeAlertPlusDbContext>();

            context.Database.EnsureCreated();

            var adminEmail = "admin@gmail.com";

            if (!context.Users.Any(u => u.Email == adminEmail))
            {
                var admin = new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Nicol",
                    LastName = "Stancu",
                    Email = adminEmail,
                    IdApp = "local",
                    IsEmailConfirmed = true,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("20042005"),
                    Telephone = "0745623166",
                    CreatedAt = DateTime.UtcNow
                };

                context.Users.Add(admin);
                context.SaveChanges();
            }
        }
    }
}