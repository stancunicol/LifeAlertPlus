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

            // Ensure the Invitations table exists even when the DB was created without EF migrations.
            // (EnsureCreated doesn't evolve schema, so older DBs might miss newer tables.)
            await EnsureInvitationsTableAsync(context);
            await EnsureUserSpO2ColumnsAsync(context);
            await EnsureMonitoredSpO2ColumnsAsync(context);

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
                new() { Id = Guid.NewGuid(), Name = "Admin", CreatedAt = DateTime.UtcNow },
                new() { Id = Guid.NewGuid(), Name = "User", CreatedAt = DateTime.UtcNow }
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
                    CreatedAt = DateTime.UtcNow,
                    MinHeartRate = 60,
                    MaxHeartRate = 100,
                    MinTemperature = 36.0,
                    MaxTemperature = 37.5,
                    MinSpO2 = 95,
                    MaxSpO2 = 100,
                    Language = "ro",
                    FontSize = "medium",
                    UpdateFrequency = 30,
                    NotifyByEmail = true,
                    NotifyByPush = true
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
                    CreatedAt = DateTime.UtcNow,
                    MinHeartRate = 60,
                    MaxHeartRate = 100,
                    MinTemperature = 36.0,
                    MaxTemperature = 37.5,
                    MinSpO2 = 95,
                    MaxSpO2 = 100,
                    Language = "ro",
                    FontSize = "medium",
                    UpdateFrequency = 30,
                    NotifyByEmail = true,
                    NotifyByPush = true
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

                // Seed ActivityProfile: rutina zilnica a Mariei Popescu (72 ani)
                // Valori realiste pentru o persoana varstnica: somn 22-06, siesta 13-15
                var activityProfiles = new List<ActivityProfile>
                {
                    // Somn profund (00-05)
                    new() { IdMonitored = monitored.Id, HourOfDay =  0, AveragePulse = 58, MovementRate = 0.05, SleepProbability = 0.92, DataPoints = 14, LastUpdated = DateTime.UtcNow },
                    new() { IdMonitored = monitored.Id, HourOfDay =  1, AveragePulse = 57, MovementRate = 0.04, SleepProbability = 0.94, DataPoints = 14, LastUpdated = DateTime.UtcNow },
                    new() { IdMonitored = monitored.Id, HourOfDay =  2, AveragePulse = 57, MovementRate = 0.04, SleepProbability = 0.95, DataPoints = 14, LastUpdated = DateTime.UtcNow },
                    new() { IdMonitored = monitored.Id, HourOfDay =  3, AveragePulse = 58, MovementRate = 0.05, SleepProbability = 0.94, DataPoints = 14, LastUpdated = DateTime.UtcNow },
                    new() { IdMonitored = monitored.Id, HourOfDay =  4, AveragePulse = 59, MovementRate = 0.06, SleepProbability = 0.90, DataPoints = 14, LastUpdated = DateTime.UtcNow },
                    new() { IdMonitored = monitored.Id, HourOfDay =  5, AveragePulse = 60, MovementRate = 0.08, SleepProbability = 0.85, DataPoints = 14, LastUpdated = DateTime.UtcNow },
                    // Trezire (06)
                    new() { IdMonitored = monitored.Id, HourOfDay =  6, AveragePulse = 63, MovementRate = 0.22, SleepProbability = 0.55, DataPoints = 14, LastUpdated = DateTime.UtcNow },
                    // Rutina matinala - mic dejun (07-08)
                    new() { IdMonitored = monitored.Id, HourOfDay =  7, AveragePulse = 68, MovementRate = 0.48, SleepProbability = 0.08, DataPoints = 14, LastUpdated = DateTime.UtcNow },
                    new() { IdMonitored = monitored.Id, HourOfDay =  8, AveragePulse = 72, MovementRate = 0.55, SleepProbability = 0.05, DataPoints = 14, LastUpdated = DateTime.UtcNow },
                    // Activitate matinala (09-11)
                    new() { IdMonitored = monitored.Id, HourOfDay =  9, AveragePulse = 74, MovementRate = 0.58, SleepProbability = 0.04, DataPoints = 14, LastUpdated = DateTime.UtcNow },
                    new() { IdMonitored = monitored.Id, HourOfDay = 10, AveragePulse = 73, MovementRate = 0.56, SleepProbability = 0.04, DataPoints = 14, LastUpdated = DateTime.UtcNow },
                    new() { IdMonitored = monitored.Id, HourOfDay = 11, AveragePulse = 71, MovementRate = 0.50, SleepProbability = 0.05, DataPoints = 14, LastUpdated = DateTime.UtcNow },
                    // Pranz (12)
                    new() { IdMonitored = monitored.Id, HourOfDay = 12, AveragePulse = 70, MovementRate = 0.42, SleepProbability = 0.07, DataPoints = 14, LastUpdated = DateTime.UtcNow },
                    // Siesta post-pranz (13-14)
                    new() { IdMonitored = monitored.Id, HourOfDay = 13, AveragePulse = 66, MovementRate = 0.22, SleepProbability = 0.45, DataPoints = 14, LastUpdated = DateTime.UtcNow },
                    new() { IdMonitored = monitored.Id, HourOfDay = 14, AveragePulse = 63, MovementRate = 0.14, SleepProbability = 0.60, DataPoints = 14, LastUpdated = DateTime.UtcNow },
                    // Activitate dupa-amiaza (15-17)
                    new() { IdMonitored = monitored.Id, HourOfDay = 15, AveragePulse = 68, MovementRate = 0.38, SleepProbability = 0.25, DataPoints = 14, LastUpdated = DateTime.UtcNow },
                    new() { IdMonitored = monitored.Id, HourOfDay = 16, AveragePulse = 70, MovementRate = 0.45, SleepProbability = 0.08, DataPoints = 14, LastUpdated = DateTime.UtcNow },
                    new() { IdMonitored = monitored.Id, HourOfDay = 17, AveragePulse = 69, MovementRate = 0.40, SleepProbability = 0.09, DataPoints = 14, LastUpdated = DateTime.UtcNow },
                    // Seara (18-20)
                    new() { IdMonitored = monitored.Id, HourOfDay = 18, AveragePulse = 67, MovementRate = 0.30, SleepProbability = 0.12, DataPoints = 14, LastUpdated = DateTime.UtcNow },
                    new() { IdMonitored = monitored.Id, HourOfDay = 19, AveragePulse = 66, MovementRate = 0.22, SleepProbability = 0.15, DataPoints = 14, LastUpdated = DateTime.UtcNow },
                    new() { IdMonitored = monitored.Id, HourOfDay = 20, AveragePulse = 65, MovementRate = 0.18, SleepProbability = 0.22, DataPoints = 14, LastUpdated = DateTime.UtcNow },
                    // Pre-somn (21)
                    new() { IdMonitored = monitored.Id, HourOfDay = 21, AveragePulse = 63, MovementRate = 0.10, SleepProbability = 0.48, DataPoints = 14, LastUpdated = DateTime.UtcNow },
                    // Adormire (22-23)
                    new() { IdMonitored = monitored.Id, HourOfDay = 22, AveragePulse = 61, MovementRate = 0.07, SleepProbability = 0.78, DataPoints = 14, LastUpdated = DateTime.UtcNow },
                    new() { IdMonitored = monitored.Id, HourOfDay = 23, AveragePulse = 59, MovementRate = 0.06, SleepProbability = 0.88, DataPoints = 14, LastUpdated = DateTime.UtcNow },
                };
                context.ActivityProfiles.AddRange(activityProfiles);
                await context.SaveChangesAsync();
            }
                }

        private static async Task EnsureUserSpO2ColumnsAsync(LifeAlertPlusDbContext context)
        {
            // SQLite ALTER TABLE ADD COLUMN fails if the column already exists; swallow that expected error.
            try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Users ADD COLUMN MinSpO2 INTEGER;"); } catch (Exception) { }
            try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Users ADD COLUMN MaxSpO2 INTEGER;"); } catch (Exception) { }
        }

        private static async Task EnsureMonitoredSpO2ColumnsAsync(LifeAlertPlusDbContext context)
        {
            // SQLite ALTER TABLE ADD COLUMN fails if the column already exists; swallow that expected error.
            try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Monitoreds ADD COLUMN MinSpO2 INTEGER;"); } catch (Exception) { }
            try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Monitoreds ADD COLUMN MaxSpO2 INTEGER;"); } catch (Exception) { }
        }

        private static async Task EnsureInvitationsTableAsync(LifeAlertPlusDbContext context)
        {
            // SQLite DDL: safe to run multiple times.
            const string createTableSql = @"
CREATE TABLE IF NOT EXISTS Invitations (
    Id TEXT NOT NULL CONSTRAINT PK_Invitations PRIMARY KEY,
    DoctorEmail TEXT NOT NULL,
    PatientId TEXT NOT NULL,
    Token TEXT NOT NULL,
    ExpiresAt TEXT NOT NULL,
    IsAccepted INTEGER NOT NULL,
    CreatedAt TEXT NOT NULL
);";

            const string createTokenIndexSql = @"
CREATE UNIQUE INDEX IF NOT EXISTS IX_Invitations_Token ON Invitations(Token);";

            await context.Database.ExecuteSqlRawAsync(createTableSql);
            await context.Database.ExecuteSqlRawAsync(createTokenIndexSql);
        }
    }
}