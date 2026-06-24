using FluentAssertions;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Infrastructure.Repositories;
using LifeAlertPlus.Tests.Helpers;

namespace LifeAlertPlus.Tests.Integration;

// Teste de integrare pentru MeasurementRepository — rulează pe DB reală (SQLite in-memory sau PostgreSQL)
public class MeasurementRepositoryTests : IDisposable
{
    private readonly LifeAlertPlus.Infrastructure.Context.LifeAlertPlusDbContext _ctx;
    private readonly MeasurementRepository _sut; // SUT = System Under Test
    private readonly Guid _monitoredId = Guid.NewGuid();

    public MeasurementRepositoryTests()
    {
        _ctx = TestDataFactory.CreateInMemoryDbContext();
        _sut = new MeasurementRepository(_ctx);
        SeedMonitored();
    }

    // O măsurătoare necesită un pacient existent în DB (constrângere FK pe IdMonitored)
    private void SeedMonitored()
    {
        var monitored = TestDataFactory.CreateMonitored(_monitoredId);
        _ctx.Monitoreds.Add(monitored);
        _ctx.SaveChanges();
    }

    public void Dispose() => _ctx.Dispose();

    // ── AddMeasurementAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task AddMeasurementAsync_PersistsMeasurement()
    {
        var m = TestDataFactory.CreateMeasurement(_monitoredId);
        await _sut.AddMeasurementAsync(m);

        _ctx.Measurements.Should().ContainSingle(x => x.Id == m.Id);
    }

    // ── GetMeasurementsByMonitoredIdAsync ────────────────────────────────────

    [Fact]
    public async Task GetByMonitoredId_ReturnsOnlyForThatMonitored()
    {
        var otherId = Guid.NewGuid();
        var otherMonitored = TestDataFactory.CreateMonitored(otherId);
        _ctx.Monitoreds.Add(otherMonitored);
        await _ctx.SaveChangesAsync();

        var m1 = TestDataFactory.CreateMeasurement(_monitoredId);
        var m2 = TestDataFactory.CreateMeasurement(otherId);
        await _sut.AddMeasurementAsync(m1);
        await _sut.AddMeasurementAsync(m2);

        var results = await _sut.GetMeasurementsByMonitoredIdAsync(_monitoredId, 1, 10);
        results.Should().HaveCount(1);
        results.Single().IdMonitored.Should().Be(_monitoredId);
    }

    [Fact]
    public async Task GetByMonitoredId_ReturnsEmpty_WhenNoneExist()
    {
        var results = await _sut.GetMeasurementsByMonitoredIdAsync(_monitoredId, 1, 10);
        results.Should().BeEmpty();
    }

    // 5 măsurători, paginate câte 3: pagina 1 → 3 rezultate, pagina 2 → restul de 2
    [Fact]
    public async Task GetByMonitoredId_PaginatesCorrectly()
    {
        for (var i = 0; i < 5; i++)
            await _sut.AddMeasurementAsync(TestDataFactory.CreateMeasurement(_monitoredId));

        var page1 = await _sut.GetMeasurementsByMonitoredIdAsync(_monitoredId, 1, 3);
        var page2 = await _sut.GetMeasurementsByMonitoredIdAsync(_monitoredId, 2, 3);

        page1.Should().HaveCount(3);
        page2.Should().HaveCount(2);
    }

    // Confirmă ordinea descrescătoare după CreatedAt (cea mai recentă măsurătoare primul element)
    [Fact]
    public async Task GetByMonitoredId_ReturnsMostRecentFirst()
    {
        var older = TestDataFactory.CreateMeasurement(_monitoredId);
        older.CreatedAt = DateTime.UtcNow.AddMinutes(-10);

        var newer = TestDataFactory.CreateMeasurement(_monitoredId);
        newer.CreatedAt = DateTime.UtcNow;

        await _sut.AddMeasurementAsync(older);
        await _sut.AddMeasurementAsync(newer);

        var results = (await _sut.GetMeasurementsByMonitoredIdAsync(_monitoredId, 1, 10)).ToList();
        results[0].Id.Should().Be(newer.Id);
        results[1].Id.Should().Be(older.Id);
    }

    // ── GetMeasurementByIdAsync ──────────────────────────────────────────────

    [Fact]
    public async Task GetMeasurementById_ReturnsCorrectMeasurement()
    {
        var m = TestDataFactory.CreateMeasurement(_monitoredId);
        await _sut.AddMeasurementAsync(m);

        var found = await _sut.GetMeasurementByIdAsync(m.Id);
        found.Should().NotBeNull();
        found!.Id.Should().Be(m.Id);
        found.Pulse.Should().Be(m.Pulse);
    }

    [Fact]
    public async Task GetMeasurementById_ReturnsNull_WhenNotFound()
    {
        var found = await _sut.GetMeasurementByIdAsync(Guid.NewGuid());
        found.Should().BeNull();
    }

    // ── GetTodayMeasurementsCountAsync ───────────────────────────────────────

    // Verifică filtrarea pe interval UTC [astăzi 00:00, mâine 00:00) — exclude măsurătoarea de ieri
    [Fact]
    public async Task GetTodayMeasurementsCount_CountsOnlyToday()
    {
        var today = TestDataFactory.CreateMeasurement(_monitoredId);
        today.CreatedAt = DateTime.UtcNow;

        var yesterday = TestDataFactory.CreateMeasurement(_monitoredId);
        yesterday.CreatedAt = DateTime.UtcNow.AddDays(-1);

        await _sut.AddMeasurementAsync(today);
        await _sut.AddMeasurementAsync(yesterday);

        var count = await _sut.GetTodayMeasurementsCountAsync();
        count.Should().Be(1);
    }

    // ── DeleteMeasurementsOlderThanAsync ─────────────────────────────────────

    // Politica de retenție: doar măsurătorile mai vechi de cutoff sunt șterse, cele recente rămân intacte
    [Fact]
    public async Task DeleteOlderThan_RemovesOnlyExpiredMeasurements()
    {
        var cutoff = DateTime.UtcNow.AddDays(-7);

        var old1 = TestDataFactory.CreateMeasurement(_monitoredId);
        old1.CreatedAt = cutoff.AddDays(-1);

        var recent = TestDataFactory.CreateMeasurement(_monitoredId);
        recent.CreatedAt = DateTime.UtcNow;

        await _sut.AddMeasurementAsync(old1);
        await _sut.AddMeasurementAsync(recent);

        var deleted = await _sut.DeleteMeasurementsOlderThanAsync(new[] { _monitoredId }, cutoff);

        deleted.Should().Be(1);
        _ctx.Measurements.Should().ContainSingle(m => m.Id == recent.Id);
    }

    [Fact]
    public async Task DeleteOlderThan_Returns0_WhenNothingToDelete()
    {
        var deleted = await _sut.DeleteMeasurementsOlderThanAsync(new[] { _monitoredId }, DateTime.UtcNow.AddDays(-30));
        deleted.Should().Be(0);
    }
}
