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

            // If this project contains migrations, apply them.
            // If there are no migrations (e.g. first-time local dev), create the database instead
            // to avoid the "pending model changes" exception when migrations are not present.
            var allMigrations = context.Database.GetMigrations();
            if (allMigrations != null && allMigrations.Any())
            {
                var pending = context.Database.GetPendingMigrations();
                if (pending != null && pending.Any())
                {
                    await context.Database.MigrateAsync();
                }
            }
            else
            {
                await context.Database.EnsureCreatedAsync();
            }

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
                    PhoneNumber = "+40746512348",
                    NotifyBySms = true,
                    CreatedAt = DateTime.UtcNow
                };

                context.Users.Add(admin);
                context.Users.Add(user);

                await context.SaveChangesAsync();

                // --- Seed a monitored person with 7 days of measurements for the user ---
                var monitored = new Monitored
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Maria",
                    LastName = "Popescu",
                    Birthdate = new DateTime(1952, 3, 15),
                    Gender = "Female",
                    Address = "Str. Florilor 12, Bucuresti",
                    UpdateFrequency = 2,
                    DeviceSerialNumber = "ESP-SEED-001",
                    MinHeartRate = 60,
                    MaxHeartRate = 100,
                    MinTemperature = 36.0,
                    MaxTemperature = 37.5,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                context.Set<Monitored>().Add(monitored);

                var userMonitored = new UserMonitored
                {
                    IdUser = user.Id,
                    IdMonitored = monitored.Id,
                    Relationship = "Grandmother"
                };
                context.Set<UserMonitored>().Add(userMonitored);

                await context.SaveChangesAsync();

                // Generate 7 days of measurements (6 per day)
                var rnd = new Random(42);
                var measurements = new List<Measurement>();
                for (int day = 6; day >= 0; day--)
                {
                    var baseDate = DateTime.UtcNow.Date.AddDays(-day);
                    var readingsPerDay = 6;
                    for (int r = 0; r < readingsPerDay; r++)
                    {
                        var hour = 8 + r * 2; // 08:00, 10:00, 12:00, 14:00, 16:00, 18:00
                        var ts = baseDate.AddHours(hour).AddMinutes(rnd.Next(0, 30));

                        var pulse = 65 + rnd.NextDouble() * 30;       // 65-95 bpm
                        var temp = 36.2 + rnd.NextDouble() * 1.2;     // 36.2-37.4
                        var spo2 = 94.0 + rnd.NextDouble() * 5.0;     // 94-99
                        var isFall = false;

                        // Inject a few anomalies for interesting interpretation
                        if (day == 3 && r == 2)
                        {
                            pulse = 110; // high HR alert
                        }
                        if (day == 1 && r == 4)
                        {
                            temp = 38.2; // fever alert
                        }
                        if (day == 5 && r == 0)
                        {
                            spo2 = 93.5; // low SpO2 alert
                        }
                        if (day == 2 && r == 3)
                        {
                            isFall = true;
                            pulse = 135; // critical: fall + high HR
                        }

                        measurements.Add(new Measurement
                        {
                            Id = Guid.NewGuid(),
                            Name = "Seeded Data",
                            Activity = isFall ? "Fall detected" : "Normal",
                            IsFall = isFall,
                            IdMonitored = monitored.Id,
                            Pulse = Math.Round(pulse, 0),
                            Temperature = Math.Round(temp, 1),
                            SpO2 = Math.Round(spo2, 1),
                            Coordinates = "44.4268,26.1025",
                            CreatedAt = ts
                        });
                    }
                }
                context.Set<Measurement>().AddRange(measurements);
                await context.SaveChangesAsync();
            }
        }
    }
}