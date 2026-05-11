using FluentAssertions;
using LifeAlertPlus.Application.Services;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Shared.DTOs.Requests.Monitored;
using LifeAlertPlus.Tests.Helpers;
using Moq;

namespace LifeAlertPlus.Tests.Unit.Application;

public class MonitoredServiceTests
{
    private readonly Mock<IMonitoredRepository> _repo = new();
    private readonly MonitoredService           _sut;

    public MonitoredServiceTests()
    {
        _sut = new MonitoredService(_repo.Object);
    }

    // ── AddMonitoredPersonAsync ──────────────────────────────────────────────

    [Fact]
    public async Task AddMonitoredPersonAsync_CreatesWithCorrectDefaults()
    {
        var dto = new MonitorCreateRequestDTO
        {
            FirstName          = "Ion",
            LastName           = "Popescu",
            Birthdate          = new DateTime(1950, 5, 10),
            Gender             = "Male",
            Address            = "Str. Test 1",
            DeviceSerialNumber = "SN-001"
        };

        _repo.Setup(r => r.AddMonitoredPersonAsync(It.IsAny<Monitored>()))
             .ReturnsAsync((Monitored m) => m);

        var result = await _sut.AddMonitoredPersonAsync(dto);

        result.Should().NotBeNull();
        result.FirstName.Should().Be("Ion");
        result.LastName.Should().Be("Popescu");
        result.DeviceSerialNumber.Should().Be("SN-001");
        result.MinHeartRate.Should().Be(60);
        result.MaxHeartRate.Should().Be(100);
        result.MinTemperature.Should().Be(36.0);
        result.MaxTemperature.Should().Be(37.5);
        result.Id.Should().NotBeEmpty();
    }

    // ── GetMonitoredPersonByIdAsync ──────────────────────────────────────────

    [Fact]
    public async Task GetMonitoredPersonById_ReturnsNull_WhenNotFound()
    {
        _repo.Setup(r => r.GetMonitoredPersonByIdAsync(It.IsAny<Guid>()))
             .ReturnsAsync((Monitored?)null);

        var result = await _sut.GetMonitoredPersonByIdAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetMonitoredPersonById_ReturnsMonitored_WhenFound()
    {
        var monitored = TestDataFactory.CreateMonitored();
        _repo.Setup(r => r.GetMonitoredPersonByIdAsync(monitored.Id))
             .ReturnsAsync(monitored);

        var result = await _sut.GetMonitoredPersonByIdAsync(monitored.Id);
        result.Should().NotBeNull();
        result!.Id.Should().Be(monitored.Id);
    }

    // ── GetAllMonitoredPeopleAsync ───────────────────────────────────────────

    [Fact]
    public async Task GetAllMonitoredPeople_ReturnsAllFromRepo()
    {
        var list = new[] { TestDataFactory.CreateMonitored(), TestDataFactory.CreateMonitored() };
        _repo.Setup(r => r.GetAllMonitoredPeopleAsync()).ReturnsAsync(list);

        var result = await _sut.GetAllMonitoredPeopleAsync();
        result.Should().HaveCount(2);
    }

    // ── DeleteMonitoredPersonAsync ───────────────────────────────────────────

    [Fact]
    public async Task DeleteMonitoredPersonAsync_DelegatesToRepository()
    {
        var id = Guid.NewGuid();
        _repo.Setup(r => r.DeleteMonitoredPersonAsync(id)).Returns(Task.CompletedTask);

        await _sut.DeleteMonitoredPersonAsync(id);

        _repo.Verify(r => r.DeleteMonitoredPersonAsync(id), Times.Once);
    }

    // ── UpdateMonitoredPersonAsync ───────────────────────────────────────────

    [Fact]
    public async Task UpdateMonitoredPersonAsync_DelegatesToRepository()
    {
        var monitored = TestDataFactory.CreateMonitored();
        _repo.Setup(r => r.UpdateMonitoredPersonAsync(monitored)).Returns(Task.CompletedTask);

        await _sut.UpdateMonitoredPersonAsync(monitored);

        _repo.Verify(r => r.UpdateMonitoredPersonAsync(monitored), Times.Once);
    }

    // ── GetMonitoredPersonByDeviceSerialNumberAsync ──────────────────────────

    [Fact]
    public async Task GetMonitoredPersonByDeviceSerial_ReturnsNull_WhenNotFound()
    {
        _repo.Setup(r => r.GetMonitoredPersonByDeviceSerialNumberAsync("SN-UNKNOWN"))
             .ReturnsAsync((Monitored?)null);

        var result = await _sut.GetMonitoredPersonByDeviceSerialNumberAsync("SN-UNKNOWN");
        result.Should().BeNull();
    }
}
