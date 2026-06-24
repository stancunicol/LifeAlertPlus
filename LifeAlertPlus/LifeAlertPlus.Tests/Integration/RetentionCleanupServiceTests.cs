using FluentAssertions;
using LifeAlertPlus.API.Services;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Infrastructure.Context;
using LifeAlertPlus.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LifeAlertPlus.Tests.Integration;

// Teste de integrare pentru RetentionCleanupService — rulează zilnic la 03:00 UTC (vezi RetentionCleanupBackgroundService)
// și gestionează două politici diferite:
//  1. Ștergerea măsurătorilor/notificărilor/istoricelor mai vechi decât DataRetentionDays per pacient (sau valoarea implicită)
//  2. Ștergerea permanentă (hard-delete) a pacienților soft-delete-uiți după expirarea perioadei de grație (GracePeriodDays)
public class RetentionCleanupServiceTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly RetentionCleanupService _sut; // SUT = System Under Test
    private readonly string _dbName = Guid.NewGuid().ToString();

    public RetentionCleanupServiceTests()
    {
        // Construim un container DI real cu DbContext înregistrat ca Scoped, ca scope.CreateScope() din RunAsync să funcționeze
        // (serviciul de producție folosește IServiceScopeFactory, nu poate primi direct un DbContext în constructor)
        var services = new ServiceCollection();
        var pgUrl = Environment.GetEnvironmentVariable("TEST_POSTGRES_URL");
        if (!string.IsNullOrEmpty(pgUrl))
            services.AddDbContext<LifeAlertPlusDbContext>(opts => opts.UseNpgsql(pgUrl));
        else
            services.AddDbContext<LifeAlertPlusDbContext>(opts =>
                opts.UseSqlite($"Data Source=file:{_dbName}?mode=memory&cache=shared"));
        _provider = services.BuildServiceProvider();

        using (var bootstrap = _provider.CreateScope())
        {
            var ctx = bootstrap.ServiceProvider.GetRequiredService<LifeAlertPlusDbContext>();
            if (pgUrl == null) ctx.Database.OpenConnection();
            ctx.Database.EnsureCreated();
        }

        _sut = new RetentionCleanupService(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            TestDataFactory.CreateLogger<RetentionCleanupService>());
    }

    public void Dispose()
    {
        using (var scope = _provider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<LifeAlertPlusDbContext>();
            ctx.Database.CloseConnection();
        }
        _provider.Dispose();
    }

    // Creează un scope DI nou de fiecare dată — simulează exact cum RetentionCleanupService obține propriul DbContext în producție
    private LifeAlertPlusDbContext NewScope() =>
        _provider.CreateScope().ServiceProvider.GetRequiredService<LifeAlertPlusDbContext>();

    // retentionDays = null simulează un pacient fără override → se aplică DefaultRetentionDays
    private async Task SeedMonitoredAsync(Guid id, int? retentionDays)
    {
        using var ctx = NewScope();
        var m = TestDataFactory.CreateMonitored(id);
        m.DataRetentionDays = retentionDays;
        ctx.Monitoreds.Add(m);
        await ctx.SaveChangesAsync();
    }

    private async Task SeedMeasurementsAsync(Guid monitoredId, params DateTime[] createdAts)
    {
        using var ctx = NewScope();
        foreach (var ts in createdAts)
        {
            var m = TestDataFactory.CreateMeasurement(monitoredId);
            m.CreatedAt = ts;
            ctx.Measurements.Add(m);
        }
        await ctx.SaveChangesAsync();
    }

    private async Task SeedNotificationsAsync(Guid monitoredId, params DateTime[] createdAts)
    {
        using var ctx = NewScope();
        foreach (var ts in createdAts)
        {
            ctx.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(),
                IdMonitored = monitoredId,
                NotificationType = "Alert",
                Message = "test",
                CreatedAt = ts
            });
        }
        await ctx.SaveChangesAsync();
    }

    private async Task SeedDailyHistoriesAsync(Guid monitoredId, params DateTime[] days)
    {
        using var ctx = NewScope();
        // DailyHistory are atât IdMonitored cât și un FK shadow MonitoredId — setăm prin proprietatea de navigare
        // ca EF Core să populeze ambele consistent (altfel shadow FK-ul ar rămâne null și constrângerea ar pica)
        var monitored = await ctx.Monitoreds.FindAsync(monitoredId)
                        ?? throw new InvalidOperationException("Seed monitored first");
        foreach (var d in days)
        {
            ctx.DailyHistories.Add(new DailyHistory
            {
                Id = Guid.NewGuid(),
                IdMonitored = monitoredId,
                Monitored = monitored,
                Day = d,
                CreatedAt = d
            });
        }
        await ctx.SaveChangesAsync();
    }

    private async Task SeedWeeklyHistoriesAsync(Guid monitoredId, params DateTime[] createdAts)
    {
        using var ctx = NewScope();
        var monitored = await ctx.Monitoreds.FindAsync(monitoredId)
                        ?? throw new InvalidOperationException("Seed monitored first");
        foreach (var ts in createdAts)
        {
            ctx.WeeklyHistories.Add(new WeeklyHistory
            {
                Id = Guid.NewGuid(),
                IdMonitored = monitoredId,
                Monitored = monitored,
                CreatedAt = ts
            });
        }
        await ctx.SaveChangesAsync();
    }

    // Pacientul are DataRetentionDays=30 (override explicit) — măsurătoarea de 60 zile e ștearsă, cea de 5 zile rămâne
    [Fact]
    public async Task RunAsync_DeletesMeasurementsOlderThanPerMonitoredRetention()
    {
        var id = Guid.NewGuid();
        await SeedMonitoredAsync(id, retentionDays: 30);

        var ancient = DateTime.UtcNow.AddDays(-60);
        var recent  = DateTime.UtcNow.AddDays(-5);
        await SeedMeasurementsAsync(id, ancient, recent);

        await _sut.RunAsync();

        using var ctx = NewScope();
        var remaining = ctx.Measurements.Where(m => m.IdMonitored == id).ToList();
        remaining.Should().ContainSingle();
        remaining[0].CreatedAt.Should().BeCloseTo(recent, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task RunAsync_KeepsRecentMeasurements()
    {
        var id = Guid.NewGuid();
        await SeedMonitoredAsync(id, retentionDays: 30);
        await SeedMeasurementsAsync(id, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(-2));

        await _sut.RunAsync();

        using var ctx = NewScope();
        ctx.Measurements.Count(m => m.IdMonitored == id).Should().Be(2);
    }

    // Fără override pe pacient (retentionDays: null), se aplică RetentionCleanupService.DefaultRetentionDays
    [Fact]
    public async Task RunAsync_UsesDefaultRetention_WhenMonitoredHasNoOverride()
    {
        var id = Guid.NewGuid();
        await SeedMonitoredAsync(id, retentionDays: null);

        var veryOld = DateTime.UtcNow.AddDays(-(RetentionCleanupService.DefaultRetentionDays + 5));
        var stillFresh = DateTime.UtcNow.AddDays(-10);
        await SeedMeasurementsAsync(id, veryOld, stillFresh);

        await _sut.RunAsync();

        using var ctx = NewScope();
        var rem = ctx.Measurements.Where(m => m.IdMonitored == id).ToList();
        rem.Should().ContainSingle();
        rem[0].CreatedAt.Should().BeCloseTo(stillFresh, TimeSpan.FromSeconds(1));
    }

    // Politica de retenție se aplică uniform pe toate tipurile de date istorice ale pacientului, nu doar pe măsurători
    [Fact]
    public async Task RunAsync_DeletesNotificationsAndHistories()
    {
        var id = Guid.NewGuid();
        await SeedMonitoredAsync(id, retentionDays: 30);

        var old = DateTime.UtcNow.AddDays(-100);
        var fresh = DateTime.UtcNow.AddDays(-5);
        await SeedNotificationsAsync(id, old, fresh);
        await SeedDailyHistoriesAsync(id, old, fresh);
        await SeedWeeklyHistoriesAsync(id, old, fresh);

        await _sut.RunAsync();

        using var ctx = NewScope();
        ctx.Notifications.Count(n => n.IdMonitored == id).Should().Be(1);
        ctx.DailyHistories.Count(d => d.IdMonitored == id).Should().Be(1);
        ctx.WeeklyHistories.Count(w => w.IdMonitored == id).Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_DoesNotTouchOtherMonitoreds()
    {
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        await SeedMonitoredAsync(idA, retentionDays: 30);
        await SeedMonitoredAsync(idB, retentionDays: 30);
        var old = DateTime.UtcNow.AddDays(-90);
        await SeedMeasurementsAsync(idA, old);
        await SeedMeasurementsAsync(idB, old);

        await _sut.RunAsync();

        using var ctx = NewScope();
        ctx.Measurements.Count(m => m.IdMonitored == idA).Should().Be(0);
        ctx.Measurements.Count(m => m.IdMonitored == idB).Should().Be(0);
    }

    // retentionDays=0 (sau negativ) e tratat ca "retenție dezactivată" — pacientul e exclus complet de la ștergere,
    // indiferent cât de vechi sunt datele (folosit pentru cazuri speciale unde datele trebuie păstrate la nesfârșit)
    [Fact]
    public async Task RunAsync_SkipsMonitored_WhenRetentionIsZeroOrNegative()
    {
        var id = Guid.NewGuid();
        await SeedMonitoredAsync(id, retentionDays: 0);
        var ancient = DateTime.UtcNow.AddDays(-10000);
        await SeedMeasurementsAsync(id, ancient);

        await _sut.RunAsync();

        using var ctx = NewScope();
        ctx.Measurements.Count(m => m.IdMonitored == id).Should().Be(1);
    }

    // Pacienții soft-delete-uiți (DeletedAt setat) sunt excluși din ștergerea de retenție normală —
    // ei sunt gestionați separat de logica de hard-delete după perioada de grație (vezi testele de mai jos)
    [Fact]
    public async Task RunAsync_IgnoresSoftDeletedMonitoreds()
    {
        var id = Guid.NewGuid();
        using (var ctx = NewScope())
        {
            var m = TestDataFactory.CreateMonitored(id);
            m.DataRetentionDays = 30;
            m.DeletedAt = DateTime.UtcNow;
            ctx.Monitoreds.Add(m);
            await ctx.SaveChangesAsync();
        }
        var old = DateTime.UtcNow.AddDays(-90);
        await SeedMeasurementsAsync(id, old);

        await _sut.RunAsync();

        using var ctx2 = NewScope();
        ctx2.Measurements.Count(m => m.IdMonitored == id).Should().Be(1);
    }

    // ── Hard-delete după perioada de grație (7 zile) ────────────────────────────────────

    // După expirarea GracePeriodDays de la soft-delete, pacientul e șters definitiv (hard-delete) din DB
    [Fact]
    public async Task RunAsync_HardDeletesMonitoredPerson_AfterGracePeriodExpires()
    {
        var id = Guid.NewGuid();
        using (var ctx = NewScope())
        {
            var m = TestDataFactory.CreateMonitored(id);
            m.DeletedAt = DateTime.UtcNow.AddDays(-(RetentionCleanupService.GracePeriodDays + 1));
            ctx.Monitoreds.Add(m);
            await ctx.SaveChangesAsync();
        }

        await _sut.RunAsync();

        using var verify = NewScope();
        verify.Monitoreds.Any(m => m.Id == id).Should().BeFalse();
    }

    // Cât timp perioada de grație nu a expirat, pacientul rămâne în DB (utilizatorul mai poate anula ștergerea / Admin poate reactiva)
    [Fact]
    public async Task RunAsync_DoesNotHardDelete_WhenGracePeriodNotYetExpired()
    {
        var id = Guid.NewGuid();
        using (var ctx = NewScope())
        {
            var m = TestDataFactory.CreateMonitored(id);
            m.DeletedAt = DateTime.UtcNow.AddDays(-3); // în interiorul ferestrei de 7 zile
            ctx.Monitoreds.Add(m);
            await ctx.SaveChangesAsync();
        }

        await _sut.RunAsync();

        using var verify = NewScope();
        verify.Monitoreds.Any(m => m.Id == id).Should().BeTrue();
    }
}
