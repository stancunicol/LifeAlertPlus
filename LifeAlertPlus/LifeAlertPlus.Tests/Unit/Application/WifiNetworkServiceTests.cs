using FluentAssertions;
using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Application.Services;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;
using Moq;

namespace LifeAlertPlus.Tests.Unit.Application;

// Teste pentru WifiNetworkService — validările de business (SSID/parolă, duplicate, limita de 3 rețele per dispozitiv)
public class WifiNetworkServiceTests
{
    private readonly Mock<IWifiNetworkRepository> _repo = new();
    private readonly WifiNetworkService _sut; // SUT = System Under Test
    private readonly Guid _monitoredId = Guid.NewGuid();

    public WifiNetworkServiceTests()
    {
        _sut = new WifiNetworkService(_repo.Object);
    }

    [Fact]
    public async Task AddAsync_Fails_WhenSsidIsEmpty()
    {
        var (ok, error, network) = await _sut.AddAsync(_monitoredId, "", "pwd");

        ok.Should().BeFalse();
        error.Should().Be("ssidRequired");
        network.Should().BeNull();
    }

    [Fact]
    public async Task AddAsync_Fails_WhenSsidIsWhitespace()
    {
        var (ok, error, _) = await _sut.AddAsync(_monitoredId, "   ", "pwd");

        ok.Should().BeFalse();
        error.Should().Be("ssidRequired");
    }

    // 33 caractere — peste limita standardului WiFi 802.11 pentru SSID (max 32)
    [Fact]
    public async Task AddAsync_Fails_WhenSsidTooLong()
    {
        var (ok, error, _) = await _sut.AddAsync(_monitoredId, new string('a', 33), "pwd");

        ok.Should().BeFalse();
        error.Should().Be("ssidTooLong");
    }

    // 65 caractere — peste limita WPA2 pentru parolă (max 64)
    [Fact]
    public async Task AddAsync_Fails_WhenPasswordTooLong()
    {
        var (ok, error, _) = await _sut.AddAsync(_monitoredId, "home", new string('p', 65));

        ok.Should().BeFalse();
        error.Should().Be("passwordTooLong");
    }

    [Fact]
    public async Task AddAsync_Fails_WhenDuplicateSsid()
    {
        _repo.Setup(r => r.GetByMonitoredIdAsync(_monitoredId))
             .ReturnsAsync(new[] { new WifiNetwork { Ssid = "home", Password = "x" } });

        var (ok, error, _) = await _sut.AddAsync(_monitoredId, "home", "newpwd");

        ok.Should().BeFalse();
        error.Should().Be("ssidDuplicate");
    }

    [Fact]
    public async Task AddAsync_Fails_WhenLimitReached()
    {
        _repo.Setup(r => r.GetByMonitoredIdAsync(_monitoredId)).ReturnsAsync(Array.Empty<WifiNetwork>());
        _repo.Setup(r => r.CountByMonitoredIdAsync(_monitoredId))
             .ReturnsAsync(IWifiNetworkService.MaxNetworksPerDevice);

        var (ok, error, _) = await _sut.AddAsync(_monitoredId, "extra", "pwd");

        ok.Should().BeFalse();
        error.Should().Be("limitReached");
    }

    // Capturăm entitatea trimisă la repository (Callback) ca să verificăm exact ce s-a construit intern, nu doar valoarea de retur
    [Fact]
    public async Task AddAsync_Succeeds_AndPersistsNetwork()
    {
        _repo.Setup(r => r.GetByMonitoredIdAsync(_monitoredId)).ReturnsAsync(Array.Empty<WifiNetwork>());
        _repo.Setup(r => r.CountByMonitoredIdAsync(_monitoredId)).ReturnsAsync(0);
        WifiNetwork? captured = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<WifiNetwork>()))
             .Callback<WifiNetwork>(n => captured = n)
             .Returns(Task.CompletedTask);

        var (ok, error, network) = await _sut.AddAsync(_monitoredId, "home", "secret");

        ok.Should().BeTrue();
        error.Should().BeNull();
        network.Should().NotBeNull();
        captured.Should().NotBeNull();
        captured!.IdMonitored.Should().Be(_monitoredId);
        captured.Ssid.Should().Be("home");
        captured.Password.Should().Be("secret");
        captured.Id.Should().NotBe(Guid.Empty);
        _repo.Verify(r => r.AddAsync(It.IsAny<WifiNetwork>()), Times.Once);
    }

    // Spațiile din jurul SSID-ului trebuie eliminate înainte de salvare (utilizatorul poate tasta accidental spații)
    [Fact]
    public async Task AddAsync_TrimsSsid()
    {
        _repo.Setup(r => r.GetByMonitoredIdAsync(_monitoredId)).ReturnsAsync(Array.Empty<WifiNetwork>());
        _repo.Setup(r => r.CountByMonitoredIdAsync(_monitoredId)).ReturnsAsync(0);
        WifiNetwork? captured = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<WifiNetwork>()))
             .Callback<WifiNetwork>(n => captured = n)
             .Returns(Task.CompletedTask);

        var (ok, _, _) = await _sut.AddAsync(_monitoredId, "  home  ", "pwd");

        ok.Should().BeTrue();
        captured!.Ssid.Should().Be("home");
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalse_WhenNotFound()
    {
        var id = Guid.NewGuid();
        _repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync((WifiNetwork?)null);

        var result = await _sut.DeleteAsync(id);

        result.Should().BeFalse();
        _repo.Verify(r => r.DeleteAsync(It.IsAny<WifiNetwork>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsTrue_WhenFoundAndDeleted()
    {
        var network = new WifiNetwork { Id = Guid.NewGuid(), Ssid = "x", Password = "y" };
        _repo.Setup(r => r.GetByIdAsync(network.Id)).ReturnsAsync(network);
        _repo.Setup(r => r.DeleteAsync(network)).Returns(Task.CompletedTask);

        var result = await _sut.DeleteAsync(network.Id);

        result.Should().BeTrue();
        _repo.Verify(r => r.DeleteAsync(network), Times.Once);
    }

    [Fact]
    public async Task GetByMonitoredIdAsync_DelegatesToRepo()
    {
        var list = new[] { new WifiNetwork { Ssid = "a" }, new WifiNetwork { Ssid = "b" } };
        _repo.Setup(r => r.GetByMonitoredIdAsync(_monitoredId)).ReturnsAsync(list);

        var result = await _sut.GetByMonitoredIdAsync(_monitoredId);

        result.Should().BeEquivalentTo(list);
    }

    [Fact]
    public async Task GetByDeviceSerialAsync_DelegatesToRepo()
    {
        var list = new[] { new WifiNetwork { Ssid = "a" } };
        _repo.Setup(r => r.GetByDeviceSerialAsync("ESP32-AAA")).ReturnsAsync(list);

        var result = await _sut.GetByDeviceSerialAsync("ESP32-AAA");

        result.Should().BeEquivalentTo(list);
    }
}
