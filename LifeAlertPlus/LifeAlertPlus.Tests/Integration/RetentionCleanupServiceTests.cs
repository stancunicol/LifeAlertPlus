using FluentAssertions;
using LifeAlertPlus.API.Services;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Infrastructure.Context;
using LifeAlertPlus.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LifeAlertPlus.Tests.Integration;

public class RetentionCleanupServiceTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly RetentionCleanupService _sut;
    private readonly string _dbName = Guid.NewGuid().ToString();

    public RetentionCleanupServiceTests()
    {
        // Build a DI container with the DbContext as scoped, so RunAsync's scope.CreateScope() works.
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

    private LifeAlertPlusDbContext NewScope() =>
        _provider.CreateScope().ServiceProvider.GetRequiredService<LifeAlertPlusDbContext>();

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
        // DailyHistory has both IdMonitored and a shadow MonitoredId FK — set via navigation
        // so EF populates both consistently.
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
}
