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
            var userEmail = "stancunicol3@gmail.com";

            if (!await context.Users.AnyAsync(u => u.Email == adminEmail))
            {
                var adminRoleId = (await context.Roles.FirstAsync(r => r.Name == "Admin")).Id;
                var admin = new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Admin",
                    LastName = "User",
                    Email = adminEmail,
                    RoleId = adminRoleId,
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
                    UpdateFrequency = 30,
                    NotifyByEmail = true,
                    NotifyByPush = true
                };
                context.Users.Add(admin);
            }

            if (!await context.Users.AnyAsync(u => u.Email == userEmail))
            {
                var userRoleId = (await context.Roles.FirstAsync(r => r.Name == "User")).Id;
                var user = new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Nicol",
                    LastName = "Stancu",
                    Email = userEmail,
                    RoleId = userRoleId,
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
                    UpdateFrequency = 30,
                    NotifyByEmail = true,
                    NotifyByPush = true
                };
                context.Users.Add(user);
            }

            await context.SaveChangesAsync();

            // ── Seed monitored people for the regular user, idempotently per serial ──
            var seededUser = await context.Users.FirstOrDefaultAsync(u => u.Email == userEmail);
            if (seededUser != null)
            {
                foreach (var cfg in BuildMonitoredConfigs())
                {
                    await SeedMonitoredPersonAsync(context, seededUser.Id, cfg);
                }
            }
        }

        // ────────────────────────────────────────────────────────────────────────
        //  Monitored seed configuration
        // ────────────────────────────────────────────────────────────────────────

        private enum LifestylePreset
        {
            Stable,        // regular routine, mid-range vitals
            Hypertensive,  // elevated baseline HR, occasional spikes
            Sedentary,     // low movement, long afternoon nap
            Active,        // early-morning walker, higher daytime HR
            Frail,         // very low activity, fragile vitals
            Archived       // moved to permanent care; data frozen 30 days ago
        }

        private sealed record SeedMonitoredConfig(
            string FirstName,
            string LastName,
            DateTime Birthdate,
            string Gender,
            string Address,
            string Serial,
            int? MinHr,
            int? MaxHr,
            double? MinTemp,
            double? MaxTemp,
            LifestylePreset Preset,
            int RandomSeed,
            bool IsArchived,
            int? ArchivedDaysAgo);

        private static List<SeedMonitoredConfig> BuildMonitoredConfigs() => new()
        {
            // Existing seed (kept so older DBs preserve their data identity).
            new(
                FirstName: "Maria",
                LastName:  "Popescu",
                Birthdate: new DateTime(1952, 3, 15),
                Gender:    "Female",
                Address:   "Str. Florilor 12, București",
                Serial:    "ESP-SEED-001",
                MinHr: 60, MaxHr: 100, MinTemp: 36.0, MaxTemp: 37.5,
                Preset: LifestylePreset.Stable,
                RandomSeed: 42,
                IsArchived: false, ArchivedDaysAgo: null),

            // Pensioner with mild hypertension — elevated resting HR, one spike.
            new(
                FirstName: "Ion",
                LastName:  "Marinescu",
                Birthdate: new DateTime(1948, 7, 22),
                Gender:    "Male",
                Address:   "Bd. Carol I 45, București",
                Serial:    "ESP-SEED-002",
                MinHr: 60, MaxHr: 105, MinTemp: 36.0, MaxTemp: 37.5,
                Preset: LifestylePreset.Hypertensive,
                RandomSeed: 17,
                IsArchived: false, ArchivedDaysAgo: null),

            // Widow living alone — sedentary, long afternoon naps, occasional low SpO2.
            new(
                FirstName: "Elena",
                LastName:  "Vasilescu",
                Birthdate: new DateTime(1942, 11, 5),
                Gender:    "Female",
                Address:   "Str. Mihai Eminescu 8, Brașov",
                Serial:    "ESP-SEED-003",
                MinHr: 58, MaxHr: 100, MinTemp: 35.8, MaxTemp: 37.5,
                Preset: LifestylePreset.Sedentary,
                RandomSeed: 91,
                IsArchived: false, ArchivedDaysAgo: null),

            // Just-retired engineer — morning walks, higher daytime activity.
            new(
                FirstName: "Gheorghe",
                LastName:  "Iliescu",
                Birthdate: new DateTime(1957, 2, 19),
                Gender:    "Male",
                Address:   "Str. Lungă 102, Brașov",
                Serial:    "ESP-SEED-004",
                MinHr: 55, MaxHr: 110, MinTemp: 36.1, MaxTemp: 37.5,
                Preset: LifestylePreset.Active,
                RandomSeed: 33,
                IsArchived: false, ArchivedDaysAgo: null),

            // Very elderly, recent fall — low activity baseline, one fall event.
            new(
                FirstName: "Ana",
                LastName:  "Dumitrescu",
                Birthdate: new DateTime(1937, 9, 30),
                Gender:    "Female",
                Address:   "Str. Republicii 27, Sibiu",
                Serial:    "ESP-SEED-005",
                MinHr: 55, MaxHr: 95, MinTemp: 35.8, MaxTemp: 37.4,
                Preset: LifestylePreset.Frail,
                RandomSeed: 58,
                IsArchived: false, ArchivedDaysAgo: null),

            // Moved to a permanent care home — archived 30 days ago, data preserved.
            new(
                FirstName: "Constantin",
                LastName:  "Rădulescu",
                Birthdate: new DateTime(1951, 4, 8),
                Gender:    "Male",
                Address:   "Str. Lalelelor 14, Cluj-Napoca",
                Serial:    "ESP-SEED-006",
                MinHr: 60, MaxHr: 100, MinTemp: 36.0, MaxTemp: 37.5,
                Preset: LifestylePreset.Archived,
                RandomSeed: 73,
                IsArchived: true, ArchivedDaysAgo: 30),
        };

        private static async Task SeedMonitoredPersonAsync(LifeAlertPlusDbContext context, Guid userId, SeedMonitoredConfig cfg)
        {
            // Idempotent: skip if a monitored with this serial already exists.
            var existing = await context.Monitoreds.FirstOrDefaultAsync(m => m.DeviceSerialNumber == cfg.Serial);
            if (existing != null)
            {
                // Make sure the link to the user still exists (covers manual deletions).
                var linkExists = await context.UserMonitoreds
                    .AnyAsync(um => um.IdUser == userId && um.IdMonitored == existing.Id);
                if (!linkExists)
                {
                    context.UserMonitoreds.Add(new UserMonitored { IdUser = userId, IdMonitored = existing.Id });
                    await context.SaveChangesAsync();
                }
                return;
            }

            var now = DateTime.UtcNow;
            var monitored = new Monitored
            {
                Id = Guid.NewGuid(),
                FirstName = cfg.FirstName,
                LastName = cfg.LastName,
                Birthdate = cfg.Birthdate,
                Gender = cfg.Gender,
                Address = cfg.Address,
                DeviceSerialNumber = cfg.Serial,
                UpdateFrequency = 2,
                MinHeartRate = cfg.MinHr,
                MaxHeartRate = cfg.MaxHr,
                MinTemperature = cfg.MinTemp,
                MaxTemperature = cfg.MaxTemp,
                IsActive = !cfg.IsArchived,
                IsArchived = cfg.IsArchived,
                ArchivedAt = cfg.IsArchived && cfg.ArchivedDaysAgo.HasValue
                    ? now.AddDays(-cfg.ArchivedDaysAgo.Value)
                    : null,
                CreatedAt = now
            };
            context.Monitoreds.Add(monitored);
            context.UserMonitoreds.Add(new UserMonitored { IdUser = userId, IdMonitored = monitored.Id });
            await context.SaveChangesAsync();

            // Measurements: archived people get their data frozen as of their archive date;
            // active people get rolling 7 days ending now.
            var measurementEndDate = cfg.IsArchived && cfg.ArchivedDaysAgo.HasValue
                ? now.AddDays(-cfg.ArchivedDaysAgo.Value)
                : now;

            var rnd = new Random(cfg.RandomSeed);
            var measurements = BuildMeasurementsFor(monitored.Id, cfg.Preset, measurementEndDate, rnd);
            context.Measurements.AddRange(measurements);

            var profile = BuildActivityProfileFor(monitored.Id, cfg.Preset, now);
            context.ActivityProfiles.AddRange(profile);

            await context.SaveChangesAsync();
        }

        // ────────────────────────────────────────────────────────────────────────
        //  Measurement generation — 7 days × 6 readings per day, per-preset baseline
        // ────────────────────────────────────────────────────────────────────────

        private static List<Measurement> BuildMeasurementsFor(Guid monitoredId, LifestylePreset preset, DateTime endDate, Random rnd)
        {
            // Per-preset baselines: (HR low, HR high, temp range, spo2 baseline, anomaly tuples)
            // anomalies: (day from end, reading index, kind) — kind: hr-spike, fever, low-spo2, fall
            var (hrLow, hrHigh, tempBase, spo2Base, anomalies) = preset switch
            {
                LifestylePreset.Stable => (65.0, 95.0, 36.5, 97.5, new[]
                {
                    (3, 2, "hr-spike"),
                    (1, 4, "fever"),
                    (5, 0, "low-spo2"),
                    (2, 3, "fall")
                }),
                LifestylePreset.Hypertensive => (75.0, 105.0, 36.4, 96.5, new[]
                {
                    (4, 2, "hr-spike"),
                    (2, 5, "hr-spike"),
                    (0, 3, "low-spo2")
                }),
                LifestylePreset.Sedentary => (62.0, 88.0, 36.2, 95.5, new[]
                {
                    (6, 0, "low-spo2"),
                    (3, 1, "low-spo2"),
                    (1, 5, "fever")
                }),
                LifestylePreset.Active => (60.0, 100.0, 36.6, 98.0, new[]
                {
                    (4, 0, "hr-spike"),  // morning walk peak
                    (0, 0, "hr-spike")
                }),
                LifestylePreset.Frail => (55.0, 90.0, 36.1, 95.0, new[]
                {
                    (4, 1, "fall"),
                    (4, 2, "hr-spike"),
                    (2, 3, "low-spo2"),
                    (0, 4, "low-spo2")
                }),
                LifestylePreset.Archived => (60.0, 92.0, 36.4, 97.0, new[]
                {
                    (5, 2, "hr-spike"),
                    (3, 4, "fever")
                }),
                _ => (65.0, 95.0, 36.5, 97.5, Array.Empty<(int, int, string)>())
            };

            var measurements = new List<Measurement>();
            var coords = preset switch
            {
                LifestylePreset.Stable       => "44.4268,26.1025",  // București
                LifestylePreset.Hypertensive => "44.4396,26.0963",  // București centru
                LifestylePreset.Sedentary    => "45.6427,25.5887",  // Brașov
                LifestylePreset.Active       => "45.6589,25.6109",  // Brașov
                LifestylePreset.Frail        => "45.7983,24.1256",  // Sibiu
                LifestylePreset.Archived     => "46.7712,23.6236",  // Cluj-Napoca
                _ => "44.4268,26.1025"
            };

            for (int day = 6; day >= 0; day--)
            {
                var baseDate = endDate.Date.AddDays(-day);
                var readingsPerDay = 6;
                for (int r = 0; r < readingsPerDay; r++)
                {
                    var hour = 8 + r * 2;  // 08, 10, 12, 14, 16, 18
                    var ts = baseDate.AddHours(hour).AddMinutes(rnd.Next(0, 30));

                    var pulse = hrLow + rnd.NextDouble() * (hrHigh - hrLow);
                    var temp = tempBase + rnd.NextDouble() * 1.0;
                    var spo2 = spo2Base + rnd.NextDouble() * 2.0;
                    var isFall = false;
                    var activity = "Normal";

                    // Apply anomaly injections.
                    var match = anomalies.FirstOrDefault(a => a.Item1 == day && a.Item2 == r);
                    if (match.Item3 != null)
                    {
                        switch (match.Item3)
                        {
                            case "hr-spike":
                                pulse = 115 + rnd.NextDouble() * 15;  // 115-130
                                break;
                            case "fever":
                                temp = 38.0 + rnd.NextDouble() * 0.6;
                                break;
                            case "low-spo2":
                                spo2 = 90 + rnd.NextDouble() * 3.0;   // 90-93
                                break;
                            case "fall":
                                isFall = true;
                                pulse = 130 + rnd.NextDouble() * 15;
                                activity = "Fall detected";
                                break;
                        }
                    }

                    measurements.Add(new Measurement
                    {
                        Id = Guid.NewGuid(),
                        Name = "Seeded Data",
                        Activity = activity,
                        IsFall = isFall,
                        IdMonitored = monitoredId,
                        Pulse = Math.Round(pulse, 0),
                        Temperature = Math.Round(temp, 1),
                        SpO2 = Math.Round(spo2, 1),
                        Coordinates = coords,
                        CreatedAt = ts
                    });
                }
            }
            return measurements;
        }

        // ────────────────────────────────────────────────────────────────────────
        //  Hourly activity profile generation per preset
        // ────────────────────────────────────────────────────────────────────────

        private static List<ActivityProfile> BuildActivityProfileFor(Guid monitoredId, LifestylePreset preset, DateTime now)
        {
            // Each preset returns a 24-element array of (AveragePulse, MovementRate, SleepProbability).
            var hourly = preset switch
            {
                LifestylePreset.Stable       => StableProfile(),
                LifestylePreset.Hypertensive => HypertensiveProfile(),
                LifestylePreset.Sedentary    => SedentaryProfile(),
                LifestylePreset.Active       => ActiveProfile(),
                LifestylePreset.Frail        => FrailProfile(),
                LifestylePreset.Archived     => StableProfile(),
                _ => StableProfile()
            };

            var list = new List<ActivityProfile>(24);
            for (int h = 0; h < 24; h++)
            {
                var (hr, mov, sleep) = hourly[h];
                list.Add(new ActivityProfile
                {
                    IdMonitored = monitoredId,
                    HourOfDay = h,
                    AveragePulse = hr,
                    MovementRate = mov,
                    SleepProbability = sleep,
                    DataPoints = 14,
                    LastUpdated = now
                });
            }
            return list;
        }

        // 24-hour patterns. Each tuple = (AveragePulse, MovementRate, SleepProbability).
        private static (double, double, double)[] StableProfile() => new (double, double, double)[]
        {
            (58, 0.05, 0.92), (57, 0.04, 0.94), (57, 0.04, 0.95), (58, 0.05, 0.94),  // 00-03
            (59, 0.06, 0.90), (60, 0.08, 0.85),                                       // 04-05
            (63, 0.22, 0.55),                                                         // 06 (waking)
            (68, 0.48, 0.08), (72, 0.55, 0.05),                                       // 07-08 (breakfast)
            (74, 0.58, 0.04), (73, 0.56, 0.04), (71, 0.50, 0.05),                     // 09-11 (active morning)
            (70, 0.42, 0.07),                                                         // 12 (lunch)
            (66, 0.22, 0.45), (63, 0.14, 0.60),                                       // 13-14 (siesta)
            (68, 0.38, 0.25), (70, 0.45, 0.08), (69, 0.40, 0.09),                     // 15-17 (afternoon)
            (67, 0.30, 0.12), (66, 0.22, 0.15), (65, 0.18, 0.22),                     // 18-20 (evening)
            (63, 0.10, 0.48),                                                         // 21 (pre-sleep)
            (61, 0.07, 0.78), (59, 0.06, 0.88)                                        // 22-23 (falling asleep)
        };

        // Higher resting HR throughout; less restful night.
        private static (double, double, double)[] HypertensiveProfile() => new (double, double, double)[]
        {
            (68, 0.08, 0.82), (67, 0.07, 0.85), (66, 0.06, 0.86), (67, 0.07, 0.85),
            (69, 0.10, 0.80), (72, 0.15, 0.70),
            (76, 0.30, 0.45),
            (82, 0.50, 0.10), (85, 0.58, 0.05),
            (86, 0.55, 0.04), (84, 0.50, 0.05), (82, 0.45, 0.06),
            (80, 0.40, 0.07),
            (75, 0.25, 0.35), (73, 0.18, 0.45),
            (78, 0.35, 0.22), (80, 0.42, 0.10), (79, 0.40, 0.10),
            (78, 0.32, 0.12), (77, 0.25, 0.16), (75, 0.20, 0.25),
            (73, 0.14, 0.50),
            (71, 0.10, 0.70), (69, 0.08, 0.78)
        };

        // Low daytime activity, long siesta, light early sleep.
        private static (double, double, double)[] SedentaryProfile() => new (double, double, double)[]
        {
            (56, 0.04, 0.94), (55, 0.03, 0.95), (55, 0.03, 0.95), (56, 0.04, 0.94),
            (57, 0.05, 0.92), (58, 0.07, 0.88),
            (60, 0.15, 0.65),
            (63, 0.28, 0.20), (65, 0.32, 0.10),
            (66, 0.30, 0.10), (65, 0.28, 0.12), (64, 0.22, 0.14),
            (63, 0.20, 0.18),
            (60, 0.10, 0.55), (58, 0.06, 0.72), (58, 0.05, 0.75),     // long siesta 13-15
            (62, 0.18, 0.30), (63, 0.22, 0.18),
            (62, 0.18, 0.20), (61, 0.14, 0.25), (60, 0.10, 0.40),
            (58, 0.06, 0.65),
            (57, 0.04, 0.82), (56, 0.04, 0.92)
        };

        // Higher movement, early morning walk, more cardio activity.
        private static (double, double, double)[] ActiveProfile() => new (double, double, double)[]
        {
            (54, 0.04, 0.94), (53, 0.03, 0.95), (53, 0.03, 0.95), (54, 0.04, 0.93),
            (55, 0.05, 0.90), (58, 0.10, 0.75),
            (68, 0.40, 0.20),                                          // early wake
            (88, 0.78, 0.02), (92, 0.82, 0.02),                        // morning walk 07-08
            (75, 0.55, 0.04), (72, 0.50, 0.05), (70, 0.45, 0.06),
            (68, 0.38, 0.10),
            (65, 0.25, 0.30), (62, 0.18, 0.40),                        // short rest 13-14
            (72, 0.55, 0.12), (78, 0.62, 0.04), (76, 0.58, 0.05),      // active afternoon
            (70, 0.42, 0.10), (68, 0.32, 0.14), (65, 0.22, 0.22),
            (62, 0.14, 0.42),
            (58, 0.08, 0.72), (56, 0.05, 0.86)
        };

        // Frail: very low activity, frequent rest periods, lower vitals overall.
        private static (double, double, double)[] FrailProfile() => new (double, double, double)[]
        {
            (54, 0.03, 0.95), (53, 0.03, 0.96), (53, 0.02, 0.96), (54, 0.03, 0.95),
            (55, 0.04, 0.94), (56, 0.06, 0.88),
            (58, 0.12, 0.55),
            (60, 0.20, 0.18), (62, 0.22, 0.12),
            (62, 0.20, 0.14), (61, 0.18, 0.18), (60, 0.14, 0.22),
            (60, 0.14, 0.28),
            (57, 0.08, 0.62), (55, 0.05, 0.78),                        // long siesta
            (58, 0.12, 0.42), (60, 0.18, 0.22), (59, 0.16, 0.25),
            (58, 0.12, 0.30), (57, 0.10, 0.38), (56, 0.07, 0.55),
            (54, 0.05, 0.72),
            (53, 0.04, 0.88), (52, 0.03, 0.93)
        };

    }
}
