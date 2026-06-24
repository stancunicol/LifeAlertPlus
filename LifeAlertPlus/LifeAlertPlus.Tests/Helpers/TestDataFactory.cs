using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace LifeAlertPlus.Tests.Helpers;

// Fabrică centralizată de date de test — evită duplicarea construcției entităților (User, Monitored, Measurement etc.)
// în fiecare fișier de teste unitare/integrare. Valorile implicite (praguri vitale, parolă, GUID-uri fixe) sunt
// alese arbitrar dar consistent, ca testele să fie deterministe și ușor de citit.
public static class TestDataFactory
{
    // GUID-uri fixe (nu aleatoare) pentru rolurile de test — permit teste care verifică RoleId exact, fără să-l recalculeze
    public static readonly Guid DefaultRoleId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid AdminRoleId   = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static Role CreateUserRole() => new()
    {
        Id        = DefaultRoleId,
        Name      = "User",
        CreatedAt = DateTime.UtcNow
    };

    public static Role CreateAdminRole() => new()
    {
        Id        = AdminRoleId,
        Name      = "Admin",
        CreatedAt = DateTime.UtcNow
    };

    // Utilizator de test cu parolă hash-uită real (BCrypt) — necesar pentru testele de autentificare care apelează VerifyPassword
    public static User CreateUser(Guid? id = null, string email = "test@example.com", bool emailConfirmed = true) => new()
    {
        Id                  = id ?? Guid.NewGuid(),
        RoleId              = DefaultRoleId,
        FirstName           = "Test",
        LastName            = "User",
        Email               = email,
        PasswordHash        = BCrypt.Net.BCrypt.HashPassword("Test@1234"),
        IsEmailConfirmed    = emailConfirmed,
        Provider            = "Local",
        CreatedAt           = DateTime.UtcNow,
        MinHeartRate        = 60,
        MaxHeartRate        = 100,
        MinTemperature      = 36.0,
        MaxTemperature      = 37.5,
        MinSpO2             = 95,
        MaxSpO2             = 100,
        Language            = "ro",
        UpdateFrequency     = 30,
        NotifyByEmail       = true,
        NotifyByPush        = true
    };

    // Persoană monitorizată de test cu praguri vitale standard (HR 60-100, Temp 36-37.5, SpO2 95-100)
    public static Monitored CreateMonitored(Guid? id = null) => new()
    {
        Id                 = id ?? Guid.NewGuid(),
        FirstName          = "Ion",
        LastName           = "Popescu",
        Birthdate          = new DateTime(1950, 1, 1),
        Gender             = "Male",
        Address            = "Strada Exemplu 1",
        DeviceSerialNumber = "SN-TEST-001",
        MinHeartRate       = 60,
        MaxHeartRate       = 100,
        MinTemperature     = 36.0,
        MaxTemperature     = 37.5,
        MinSpO2            = 95,
        MaxSpO2            = 100,
        IsActive           = true,
        CreatedAt          = DateTime.UtcNow
    };

    // Măsurătoare de test cu valori vitale "normale" — utilă ca bază pe care testele individuale modifică un singur câmp
    public static Measurement CreateMeasurement(Guid? monitoredId = null) => new()
    {
        Id          = Guid.NewGuid(),
        Name        = "Test",
        Activity    = "sitting",
        IsFall      = false,
        IdMonitored = monitoredId ?? Guid.NewGuid(),
        Pulse       = 75,
        Temperature = 36.8,
        SpO2        = 98,
        Coordinates = "44.4268,26.1025",
        CreatedAt   = DateTime.UtcNow
    };

    // Configurație JWT minimală pentru JwtService — cheia trebuie să aibă minim 32 bytes pentru HS256 (vezi JwtService.GenerateToken)
    public static IConfiguration CreateJwtConfiguration(string key = "super-secret-key-at-least-32-chars!!", int expiresInMinutes = 60) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"]              = key,
                ["Jwt:Issuer"]           = "LifeAlertPlus",
                ["Jwt:Audience"]         = "LifeAlertPlusClient",
                ["Jwt:ExpiresInMinutes"] = expiresInMinutes.ToString()
            })
            .Build();

    // SMS dezactivat implicit în teste (evită trimiteri reale către Twilio la rularea suitei de teste)
    public static IConfiguration CreateAlertMonitorConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AlertMonitor:SendCriticalSmsImmediately"] = "false",
                ["AlertMonitor:SendAlertSmsImmediately"]    = "false"
            })
            .Build();

    // Dacă e setată variabila de mediu TEST_POSTGRES_URL, testele rulează pe PostgreSQL real.
    // Altfel, se folosește SQLite in-memory pentru rulare locală fără server.
    public static LifeAlertPlusDbContext CreateInMemoryDbContext(string? dbName = null)
    {
        var pgUrl = Environment.GetEnvironmentVariable("TEST_POSTGRES_URL");

        DbContextOptions<LifeAlertPlusDbContext> options;
        if (!string.IsNullOrEmpty(pgUrl))
        {
            // Fiecare test primește un schema unic pentru izolare
            var schema = $"test_{(dbName ?? Guid.NewGuid().ToString("N")[..8]).Replace("-", "_")}";
            options = new DbContextOptionsBuilder<LifeAlertPlusDbContext>()
                .UseNpgsql(pgUrl, o => o.MigrationsHistoryTable("__EFMigrationsHistory", schema))
                .Options;
        }
        else
        {
            options = new DbContextOptionsBuilder<LifeAlertPlusDbContext>()
                .UseSqlite($"Data Source=file:{dbName ?? Guid.NewGuid().ToString()}?mode=memory&cache=shared")
                .Options;
        }

        var ctx = new LifeAlertPlusDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    // Logger mock care nu scrie nicăieri — folosit când constructorul serviciului testat necesită un ILogger<T>, dar testul nu verifică logarea
    public static ILogger<T> CreateLogger<T>() => Mock.Of<ILogger<T>>();
}
