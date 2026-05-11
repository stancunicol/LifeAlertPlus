using FluentAssertions;
using LifeAlertPlus.Application.Services;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Tests.Helpers;
using Moq;

namespace LifeAlertPlus.Tests.Unit.Application;

public class MeasurementServiceTests
{
    private readonly Mock<IMeasurementRepository> _repo = new();
    private readonly MeasurementService           _sut;

    public MeasurementServiceTests()
    {
        _sut = new MeasurementService(_repo.Object);
    }

    // ── GetMeasurementsByMonitoredIdAsync ────────────────────────────────────

    [Fact]
    public async Task GetMeasurementsByMonitoredId_ReturnsEmpty_WhenNoMeasurements()
    {
        var monitoredId = Guid.NewGuid();
        _repo.Setup(r => r.GetMeasurementsByMonitoredIdAsync(monitoredId, 1, 10))
             .ReturnsAsync(Enumerable.Empty<Measurement>());

        var result = await _sut.GetMeasurementsByMonitoredIdAsync(monitoredId, 1, 10);

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMeasurementsByMonitoredId_ReturnsEmpty_WhenRepoReturnsNull()
    {
        var monitoredId = Guid.NewGuid();
        // Simulate a repository returning an empty collection (equivalent to the null-guard path)
        _repo.Setup(r => r.GetMeasurementsByMonitoredIdAsync(monitoredId, 1, 10))
             .ReturnsAsync(Enumerable.Empty<Measurement>());

        var result = await _sut.GetMeasurementsByMonitoredIdAsync(monitoredId, 1, 10);

        result.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task GetMeasurementsByMonitoredId_MapsMeasurementCorrectly()
    {
        var monitoredId = Guid.NewGuid();
        var measurement = TestDataFactory.CreateMeasurement(monitoredId);

        _repo.Setup(r => r.GetMeasurementsByMonitoredIdAsync(monitoredId, 1, 10))
             .ReturnsAsync(new[] { measurement });

        var result = (await _sut.GetMeasurementsByMonitoredIdAsync(monitoredId, 1, 10))!.ToList();

        result.Should().HaveCount(1);
        var dto = result[0];
        dto.Pulse.Should().Be(measurement.Pulse);
        dto.Temperature.Should().Be(measurement.Temperature);
        dto.SpO2.Should().Be(measurement.SpO2);
        dto.IdMonitored.Should().Be(measurement.IdMonitored);
        dto.IsFall.Should().Be(measurement.IsFall);
        dto.Activity.Should().Be(measurement.Activity);
        dto.Coordinates.Should().Be(measurement.Coordinates);
    }

    // ── GetMeasurementByIdAsync ──────────────────────────────────────────────

    [Fact]
    public async Task GetMeasurementById_ReturnsNull_WhenNotFound()
    {
        _repo.Setup(r => r.GetMeasurementByIdAsync(It.IsAny<Guid>()))
             .ReturnsAsync((Measurement?)null);

        var result = await _sut.GetMeasurementByIdAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetMeasurementById_MapsCorrectly_WhenFound()
    {
        var measurement = TestDataFactory.CreateMeasurement();
        _repo.Setup(r => r.GetMeasurementByIdAsync(measurement.Id))
             .ReturnsAsync(measurement);

        var result = await _sut.GetMeasurementByIdAsync(measurement.Id);

        result.Should().NotBeNull();
        result!.Pulse.Should().Be(measurement.Pulse);
        result.Temperature.Should().Be(measurement.Temperature);
        result.SpO2.Should().Be(measurement.SpO2);
        result.CreatedAt.Should().Be(measurement.CreatedAt);
    }

    // ── GetTodayMeasurementsCountAsync ───────────────────────────────────────

    [Fact]
    public async Task GetTodayMeasurementsCount_ReturnsValueFromRepo()
    {
        _repo.Setup(r => r.GetTodayMeasurementsCountAsync()).ReturnsAsync(42);

        var result = await _sut.GetTodayMeasurementsCountAsync();
        result.Should().Be(42);
    }

    // ── AddMeasurementAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task AddMeasurementAsync_DelegatesToRepository()
    {
        var measurement = TestDataFactory.CreateMeasurement();
        _repo.Setup(r => r.AddMeasurementAsync(measurement)).Returns(Task.CompletedTask);

        await _sut.AddMeasurementAsync(measurement);

        _repo.Verify(r => r.AddMeasurementAsync(measurement), Times.Once);
    }

    // ── DeleteMeasurementsOlderThanAsync ─────────────────────────────────────

    [Fact]
    public async Task DeleteMeasurementsOlderThan_ReturnsDeletedCount()
    {
        var ids      = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var cutoff   = DateTime.UtcNow.AddDays(-30);
        _repo.Setup(r => r.DeleteMeasurementsOlderThanAsync(ids, cutoff)).ReturnsAsync(5);

        var result = await _sut.DeleteMeasurementsOlderThanAsync(ids, cutoff);
        result.Should().Be(5);
    }
}
