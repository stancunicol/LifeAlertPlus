using FluentAssertions;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Infrastructure.Repositories;
using LifeAlertPlus.Tests.Helpers;

namespace LifeAlertPlus.Tests.Integration;

public class WifiNetworkRepositoryTests : IDisposable
{
    private readonly LifeAlertPlus.Infrastructure.Context.LifeAlertPlusDbContext _ctx;
    private readonly WifiNetworkRepository _sut;
    private readonly Monitored _monitored;

    public WifiNetworkRepositoryTests()
    {
        _ctx = TestDataFactory.CreateInMemoryDbContext();
        _sut = new WifiNetworkRepository(_ctx);
        _monitored = TestDataFactory.CreateMonitored();
        _ctx.Monitoreds.Add(_monitored);
        _ctx.SaveChanges();
    }

    public void Dispose() => _ctx.Dispose();

    private WifiNetwork NewNetwork(string ssid, string pass = "p", Guid? monitoredId = null) => new()
    {
        Id = Guid.NewGuid(),
        IdMonitored = monitoredId ?? _monitored.Id,
        Ssid = ssid,
        Password = pass,
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task AddAsync_PersistsAndIsRetrievable()
    {
        var n = NewNetwork("home");
        await _sut.AddAsync(n);

        var found = await _sut.GetByIdAsync(n.Id);
        found.Should().NotBeNull();
        found!.Ssid.Should().Be("home");
    }

    [Fact]
    public async Task GetByMonitoredIdAsync_ReturnsInsertionOrder()
    {
        var first = NewNetwork("first");
        first.CreatedAt = DateTime.UtcNow.AddMinutes(-2);
        var second = NewNetwork("second");
        second.CreatedAt = DateTime.UtcNow.AddMinutes(-1);

        await _sut.AddAsync(second);
        await _sut.AddAsync(first);

        var list = (await _sut.GetByMonitoredIdAsync(_monitored.Id)).ToList();
        list.Should().HaveCount(2);
        list[0].Ssid.Should().Be("first");
        list[1].Ssid.Should().Be("second");
    }

    [Fact]
    public async Task GetByMonitoredIdAsync_FiltersByMonitored()
    {
        var other = TestDataFactory.CreateMonitored();
        other.DeviceSerialNumber = "SN-OTHER";
        _ctx.Monitoreds.Add(other);
        await _ctx.SaveChangesAsync();

        await _sut.AddAsync(NewNetwork("mine"));
        await _sut.AddAsync(NewNetwork("theirs", monitoredId: other.Id));

        var mine = await _sut.GetByMonitoredIdAsync(_monitored.Id);
        mine.Should().ContainSingle().Which.Ssid.Should().Be("mine");
    }

    [Fact]
    public async Task GetByDeviceSerialAsync_JoinsThroughMonitored()
    {
        await _sut.AddAsync(NewNetwork("home", "secret"));

        var result = (await _sut.GetByDeviceSerialAsync(_monitored.DeviceSerialNumber)).ToList();

        result.Should().ContainSingle();
        result[0].Ssid.Should().Be("home");
        result[0].Password.Should().Be("secret");
    }

    [Fact]
    public async Task GetByDeviceSerialAsync_ReturnsEmpty_WhenSerialUnknown()
    {
        await _sut.AddAsync(NewNetwork("home"));

        var result = await _sut.GetByDeviceSerialAsync("SN-DOES-NOT-EXIST");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CountByMonitoredIdAsync_ReturnsExactCount()
    {
        await _sut.AddAsync(NewNetwork("a"));
        await _sut.AddAsync(NewNetwork("b"));

        var count = await _sut.CountByMonitoredIdAsync(_monitored.Id);

        count.Should().Be(2);
    }

    [Fact]
    public async Task DeleteAsync_RemovesEntity()
    {
        var n = NewNetwork("home");
        await _sut.AddAsync(n);

        await _sut.DeleteAsync(n);

        var found = await _sut.GetByIdAsync(n.Id);
        found.Should().BeNull();
    }
}
