using System.Security.Claims;
using FluentAssertions;
using LifeAlertPlus.API.Controllers;
using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Shared.DTOs.Requests.Wifi;
using LifeAlertPlus.Shared.DTOs.Responses.Wifi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace LifeAlertPlus.Tests.Unit.API.Controllers;

// Teste pentru WifiController — verifică autorizarea (un utilizator poate gestiona doar rețelele propriilor pacienți)
// și că parola nu se scurge necontrolat în răspunsurile API
public class WifiControllerTests
{
    private readonly Mock<IWifiNetworkService> _wifiSvc = new();
    private readonly Mock<IUserMonitoredService> _userMonitoredSvc = new();
    private readonly Guid _callerId = Guid.NewGuid();
    private readonly WifiController _sut; // SUT = System Under Test

    public WifiControllerTests()
    {
        // Simulăm un utilizator autentificat, cu ID-ul în claim-ul NameIdentifier (cum ar veni dintr-un JWT real)
        _sut = new WifiController(_wifiSvc.Object, _userMonitoredSvc.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, _callerId.ToString()) }, "Test"))
                }
            }
        };
    }

    // Simulează că utilizatorul curent are dreptul de a gestiona rețelele acestui pacient
    private void GrantOwnership(Guid monitoredId)
    {
        _userMonitoredSvc
            .Setup(s => s.UserOwnsMonitoredAsync(_callerId, monitoredId))
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task GetByMonitored_Returns403_WhenNotOwner()
    {
        var id = Guid.NewGuid();
        _userMonitoredSvc.Setup(s => s.GetMonitoredPeopleByUserIdAsync(_callerId))
                         .ReturnsAsync(Array.Empty<Monitored>());

        var result = await _sut.GetByMonitored(id);

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task GetByMonitored_Returns200_WithMappedDtos()
    {
        var id = Guid.NewGuid();
        GrantOwnership(id);
        _wifiSvc.Setup(s => s.GetByMonitoredIdAsync(id))
                .ReturnsAsync(new[]
                {
                    new WifiNetwork { Id = Guid.NewGuid(), Ssid = "home", Password = "secret", CreatedAt = DateTime.UtcNow }
                });

        var result = await _sut.GetByMonitored(id);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var list = ok.Value as List<WifiNetworkResponseDTO>;
        list.Should().NotBeNull();
        list!.Should().ContainSingle().Which.Ssid.Should().Be("home");
        // Parola NU trebuie să se scurgă către clientul web (WifiNetworkResponseDTO nu are câmp Password)
        list[0].Should().NotBeNull();
    }

    [Fact]
    public async Task Add_Returns400_WhenRequestNullOrMissingId()
    {
        var result = await _sut.Add(new WifiNetworkRequestDTO());
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Add_Returns403_WhenNotOwner()
    {
        var monitoredId = Guid.NewGuid();
        _userMonitoredSvc.Setup(s => s.GetMonitoredPeopleByUserIdAsync(_callerId))
                         .ReturnsAsync(Array.Empty<Monitored>());

        var result = await _sut.Add(new WifiNetworkRequestDTO { IdMonitored = monitoredId, Ssid = "x" });

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Add_Returns400_WithErrorKey_WhenServiceRejects()
    {
        var monitoredId = Guid.NewGuid();
        GrantOwnership(monitoredId);
        _wifiSvc.Setup(s => s.AddAsync(monitoredId, "x", "p"))
                .ReturnsAsync((false, "limitReached", (WifiNetwork?)null));

        var result = await _sut.Add(new WifiNetworkRequestDTO { IdMonitored = monitoredId, Ssid = "x", Password = "p" });

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Add_Returns200_OnSuccess()
    {
        var monitoredId = Guid.NewGuid();
        GrantOwnership(monitoredId);
        var network = new WifiNetwork { Id = Guid.NewGuid(), Ssid = "x", Password = "p", CreatedAt = DateTime.UtcNow };
        _wifiSvc.Setup(s => s.AddAsync(monitoredId, "x", "p"))
                .ReturnsAsync((true, (string?)null, network));

        var result = await _sut.Add(new WifiNetworkRequestDTO { IdMonitored = monitoredId, Ssid = "x", Password = "p" });

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value as WifiNetworkResponseDTO;
        dto.Should().NotBeNull();
        dto!.Id.Should().Be(network.Id);
        dto.Ssid.Should().Be("x");
    }

    [Fact]
    public async Task Delete_Returns404_WhenNotFound()
    {
        var id = Guid.NewGuid();
        _wifiSvc.Setup(s => s.GetByIdAsync(id)).ReturnsAsync((WifiNetwork?)null);

        var result = await _sut.Delete(id);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Delete_Returns403_WhenNotOwner()
    {
        var id = Guid.NewGuid();
        var monitoredId = Guid.NewGuid();
        _wifiSvc.Setup(s => s.GetByIdAsync(id))
                .ReturnsAsync(new WifiNetwork { Id = id, IdMonitored = monitoredId });
        _userMonitoredSvc.Setup(s => s.GetMonitoredPeopleByUserIdAsync(_callerId))
                         .ReturnsAsync(Array.Empty<Monitored>());

        var result = await _sut.Delete(id);

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Delete_Returns200_WhenOwnerAndDeleted()
    {
        var id = Guid.NewGuid();
        var monitoredId = Guid.NewGuid();
        _wifiSvc.Setup(s => s.GetByIdAsync(id))
                .ReturnsAsync(new WifiNetwork { Id = id, IdMonitored = monitoredId });
        GrantOwnership(monitoredId);
        _wifiSvc.Setup(s => s.DeleteAsync(id)).ReturnsAsync(true);

        var result = await _sut.Delete(id);

        result.Should().BeOfType<OkResult>();
    }
}
