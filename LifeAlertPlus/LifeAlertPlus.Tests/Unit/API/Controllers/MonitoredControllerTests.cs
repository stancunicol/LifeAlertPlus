using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using LifeAlertPlus.API.Controllers;
using LifeAlertPlus.API.Services;
using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LifeAlertPlus.Tests.Unit.API.Controllers;

// Teste pentru MonitoredController — în special logica de "ultimul proprietar" la ștergere
// (dacă mai mulți utilizatori urmăresc un pacient, ștergerea unuia doar elimină legătura;
// dacă e ultimul, persoana e soft-delete-uită; Admin face mereu soft-delete, indiferent de nr. proprietari)
public class MonitoredControllerTests
{
    private readonly Mock<IMonitoredService>     _monitoredSvc     = new();
    private readonly Mock<IUserMonitoredService> _userMonitoredSvc = new();
    private readonly MonitoredController         _sut; // SUT = System Under Test

    private static readonly Guid UserId  = Guid.NewGuid();
    private static readonly Guid AdminId = Guid.NewGuid();

    public MonitoredControllerTests()
    {
        var alertSvc  = CreateAlertSvc();
        var auditSvc  = new AuditService(
            Mock.Of<IServiceScopeFactory>(),
            NullLogger<AuditService>.Instance);

        _sut = new MonitoredController(
            _monitoredSvc.Object,
            _userMonitoredSvc.Object,
            alertSvc,
            NullLogger<MonitoredController>.Instance,
            auditSvc);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private void SetUser(Guid id, string role = "User")
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, id.ToString()),
            new Claim(ClaimTypes.Email, "user@test.com"),
            new Claim(ClaimTypes.Role, role)
        };
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
            }
        };
    }

    // Extrage flag-ul "wasLastOwner" din răspunsul anonim al controller-ului (serializăm/deserializăm ca să citim o proprietate dinamică)
    private static bool GetWasLastOwner(IActionResult result)
    {
        var value = ((OkObjectResult)result).Value!;
        var json  = JsonSerializer.Serialize(value);
        return JsonDocument.Parse(json).RootElement.GetProperty("wasLastOwner").GetBoolean();
    }

    private static AlertMonitorService CreateAlertSvc()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AlertMonitor:SendCriticalSmsImmediately"] = "false",
                ["AlertMonitor:SendAlertSmsImmediately"]    = "false"
            })
            .Build();

        return new AlertMonitorService(
            Mock.Of<IServiceScopeFactory>(),
            NullLogger<AlertMonitorService>.Instance,
            cfg,
            Mock.Of<IPushNotificationService>(),
            new ActivityProfileService(Mock.Of<IServiceScopeFactory>(), NullLogger<ActivityProfileService>.Instance),
            new ConditionRuleEngine(Mock.Of<IServiceScopeFactory>(), NullLogger<ConditionRuleEngine>.Instance),
            new NearestHospitalService(Mock.Of<IHttpClientFactory>(), NullLogger<NearestHospitalService>.Instance),
            new DeviceTestLogService(cfg));
    }

    // ── RemoveMonitoredPerson ─────────────────────────────────────────────────

    [Fact]
    public async Task RemoveMonitoredPerson_Returns401_WhenNotAuthenticated()
    {
        _sut.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        var result = await _sut.RemoveMonitoredPerson(Guid.NewGuid());

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task RemoveMonitoredPerson_Returns404_WhenMonitoredNotFound()
    {
        SetUser(UserId);
        _monitoredSvc.Setup(s => s.GetMonitoredPersonByIdAsync(It.IsAny<Guid>()))
                     .ReturnsAsync((Monitored?)null);

        var result = await _sut.RemoveMonitoredPerson(Guid.NewGuid());

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task RemoveMonitoredPerson_Returns403_WhenUserDoesNotOwnMonitored()
    {
        SetUser(UserId);
        var id = Guid.NewGuid();
        _monitoredSvc.Setup(s => s.GetMonitoredPersonByIdAsync(id))
                     .ReturnsAsync(TestDataFactory.CreateMonitored(id));
        _userMonitoredSvc.Setup(s => s.UserOwnsMonitoredAsync(UserId, id)).ReturnsAsync(false);

        var result = await _sut.RemoveMonitoredPerson(id);

        result.Should().BeOfType<ForbidResult>();
    }

    // Singurul proprietar → ștergere reală a pacientului (soft-delete), nu doar eliminarea legăturii
    [Fact]
    public async Task RemoveMonitoredPerson_SoftDeletes_WhenUserIsLastOwner()
    {
        SetUser(UserId);
        var id = Guid.NewGuid();
        _monitoredSvc.Setup(s => s.GetMonitoredPersonByIdAsync(id))
                     .ReturnsAsync(TestDataFactory.CreateMonitored(id));
        _userMonitoredSvc.Setup(s => s.UserOwnsMonitoredAsync(UserId, id)).ReturnsAsync(true);
        _userMonitoredSvc.Setup(s => s.CountUsersForMonitoredAsync(id)).ReturnsAsync(1);
        _monitoredSvc.Setup(s => s.SoftDeleteMonitoredPersonAsync(id)).ReturnsAsync(true);

        var result = await _sut.RemoveMonitoredPerson(id);

        result.Should().BeOfType<OkObjectResult>();
        GetWasLastOwner(result).Should().BeTrue();
        _monitoredSvc.Verify(s => s.SoftDeleteMonitoredPersonAsync(id), Times.Once);
        _userMonitoredSvc.Verify(s => s.RemoveUserMonitoredLinkAsync(It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
    }

    // Mai mulți proprietari → ștergem doar legătura user↔pacient; pacientul rămâne intact pentru ceilalți îngrijitori
    [Fact]
    public async Task RemoveMonitoredPerson_RemovesLink_WhenMultipleOwnersExist()
    {
        SetUser(UserId);
        var id = Guid.NewGuid();
        _monitoredSvc.Setup(s => s.GetMonitoredPersonByIdAsync(id))
                     .ReturnsAsync(TestDataFactory.CreateMonitored(id));
        _userMonitoredSvc.Setup(s => s.UserOwnsMonitoredAsync(UserId, id)).ReturnsAsync(true);
        _userMonitoredSvc.Setup(s => s.CountUsersForMonitoredAsync(id)).ReturnsAsync(3);

        var result = await _sut.RemoveMonitoredPerson(id);

        result.Should().BeOfType<OkObjectResult>();
        GetWasLastOwner(result).Should().BeFalse();
        _userMonitoredSvc.Verify(s => s.RemoveUserMonitoredLinkAsync(UserId, id), Times.Once);
        _monitoredSvc.Verify(s => s.SoftDeleteMonitoredPersonAsync(It.IsAny<Guid>()), Times.Never);
    }

    // Adminul are mereu drept de ștergere completă, indiferent câți utilizatori urmăresc pacientul —
    // de aceea nici nu se mai apelează CountUsersForMonitoredAsync (verificare omisă intenționat pentru Admin)
    [Fact]
    public async Task RemoveMonitoredPerson_AdminAlwaysSoftDeletes_RegardlessOfOwnerCount()
    {
        SetUser(AdminId, "Admin");
        var id = Guid.NewGuid();
        _monitoredSvc.Setup(s => s.GetMonitoredPersonByIdAsync(id))
                     .ReturnsAsync(TestDataFactory.CreateMonitored(id));
        _monitoredSvc.Setup(s => s.SoftDeleteMonitoredPersonAsync(id)).ReturnsAsync(true);

        var result = await _sut.RemoveMonitoredPerson(id);

        result.Should().BeOfType<OkObjectResult>();
        GetWasLastOwner(result).Should().BeTrue();
        _monitoredSvc.Verify(s => s.SoftDeleteMonitoredPersonAsync(id), Times.Once);
        _userMonitoredSvc.Verify(s => s.CountUsersForMonitoredAsync(It.IsAny<Guid>()), Times.Never);
    }

    // ── ReactivateMonitoredPerson ─────────────────────────────────────────────

    [Fact]
    public async Task ReactivateMonitoredPerson_Returns404_WhenNotFound()
    {
        SetUser(AdminId, "Admin");
        _monitoredSvc.Setup(s => s.GetMonitoredPersonByIdAsync(It.IsAny<Guid>()))
                     .ReturnsAsync((Monitored?)null);

        var result = await _sut.ReactivateMonitoredPerson(Guid.NewGuid());

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task ReactivateMonitoredPerson_Returns400_WhenNotSoftDeleted()
    {
        SetUser(AdminId, "Admin");
        var id = Guid.NewGuid();
        _monitoredSvc.Setup(s => s.GetMonitoredPersonByIdAsync(id))
                     .ReturnsAsync(TestDataFactory.CreateMonitored(id)); // DeletedAt == null

        var result = await _sut.ReactivateMonitoredPerson(id);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ReactivateMonitoredPerson_ReturnsOk_WhenSoftDeleted()
    {
        SetUser(AdminId, "Admin");
        var id       = Guid.NewGuid();
        var monitored = TestDataFactory.CreateMonitored(id);
        monitored.DeletedAt = DateTime.UtcNow.AddDays(-1);
        _monitoredSvc.Setup(s => s.GetMonitoredPersonByIdAsync(id)).ReturnsAsync(monitored);
        _monitoredSvc.Setup(s => s.ReactivateMonitoredPersonAsync(id)).ReturnsAsync(true);

        var result = await _sut.ReactivateMonitoredPerson(id);

        result.Should().BeOfType<OkObjectResult>();
        _monitoredSvc.Verify(s => s.ReactivateMonitoredPersonAsync(id), Times.Once);
    }
}
