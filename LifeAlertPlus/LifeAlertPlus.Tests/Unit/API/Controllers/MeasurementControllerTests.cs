using System.Security.Claims;
using FluentAssertions;
using LifeAlertPlus.API.Controllers;
using LifeAlertPlus.API.Services;
using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Shared.DTOs.Requests.Measurement;
using LifeAlertPlus.Shared.DTOs.Responses.Measurement;
using LifeAlertPlus.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using Moq;

namespace LifeAlertPlus.Tests.Unit.API.Controllers;

// Teste pentru MeasurementController — validarea payload-ului de la ESP32 și autorizarea pe baza ownership-ului pacientului
public class MeasurementControllerTests
{
    private readonly Mock<IMeasurementService>   _measurementSvc   = new();
    private readonly Mock<IUserMonitoredService> _userMonitoredSvc = new();
    private readonly Guid                        _callerId         = Guid.NewGuid();
    private readonly MeasurementController       _sut; // SUT = System Under Test

    public MeasurementControllerTests()
    {
        _sut = new MeasurementController(_measurementSvc.Object, BuildAlertMonitor(), _userMonitoredSvc.Object, TestDataFactory.CreateLogger<MeasurementController>());

        // Simulăm un utilizator autentificat, ca UserOwnsMonitoredAsync să poată citi ID-ul apelantului din claims
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, _callerId.ToString()) };
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
            }
        };
    }

    // Face din _callerId proprietarul persoanei monitorizate date (necesar pentru testele care trec de verificarea de autorizare)
    private void SetupOwnership(Guid monitoredId)
    {
        _userMonitoredSvc
            .Setup(s => s.GetMonitoredPeopleByUserIdAsync(_callerId))
            .ReturnsAsync([new Monitored { Id = monitoredId }]);
    }

    // AlertMonitorService are multe dependențe — construim una reală (nu mock) cu un IServiceScopeFactory
    // care aruncă la CreateScope(), pentru că testele MeasurementController nu ajung de fapt să declanșeze alerte
    private static AlertMonitorService BuildAlertMonitor()
    {
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Throws<InvalidOperationException>();

        var conditionEngine    = new ConditionRuleEngine(scopeFactory.Object, TestDataFactory.CreateLogger<ConditionRuleEngine>());
        var activityProfileSvc = new ActivityProfileService(scopeFactory.Object, TestDataFactory.CreateLogger<ActivityProfileService>());
        var nearestHospitalSvc = new NearestHospitalService(Mock.Of<IHttpClientFactory>(), TestDataFactory.CreateLogger<NearestHospitalService>());

        var testLogSvc = new DeviceTestLogService(new ConfigurationBuilder().Build());

        return new AlertMonitorService(
            scopeFactory.Object,
            TestDataFactory.CreateLogger<AlertMonitorService>(),
            TestDataFactory.CreateAlertMonitorConfiguration(),
            Mock.Of<IPushNotificationService>(),
            activityProfileSvc,
            conditionEngine,
            nearestHospitalSvc,
            testLogSvc);
    }

    private static MeasurementRequestDTO ValidDto(Guid? monitoredId = null) => new()
    {
        Name        = "Test",
        Activity    = "sitting",
        IsFall      = false,
        IdMonitored = monitoredId ?? Guid.NewGuid(),
        Pulse       = 75,
        Temperature = 36.8,
        SpO2        = 98,
        Coordinates = "44.4268,26.1025"
    };

    // ── AddMeasurement ───────────────────────────────────────────────────────

    [Fact]
    public async Task AddMeasurement_Returns400_WhenDtoIsNull()
    {
        var result = await _sut.AddMeasurement(null!);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task AddMeasurement_Returns400_WhenNameMissing()
    {
        var dto = ValidDto();
        dto.Name = "";

        var result = await _sut.AddMeasurement(dto);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task AddMeasurement_Returns400_WhenPulseIsZero()
    {
        var dto = ValidDto();
        dto.Pulse = 0;

        var result = await _sut.AddMeasurement(dto);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task AddMeasurement_Returns400_WhenTemperatureIsZero()
    {
        var dto = ValidDto();
        dto.Temperature = 0;

        var result = await _sut.AddMeasurement(dto);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task AddMeasurement_Returns400_WhenMonitoredIdEmpty()
    {
        var result = await _sut.AddMeasurement(ValidDto(Guid.Empty));
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task AddMeasurement_Returns200_WhenValid()
    {
        var dto = ValidDto();
        SetupOwnership(dto.IdMonitored);
        _measurementSvc.Setup(s => s.AddMeasurementAsync(It.IsAny<Measurement>())).Returns(Task.CompletedTask);

        var result = await _sut.AddMeasurement(dto);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task AddMeasurement_CallsRepository_WhenValid()
    {
        var dto = ValidDto();
        SetupOwnership(dto.IdMonitored);
        _measurementSvc.Setup(s => s.AddMeasurementAsync(It.IsAny<Measurement>())).Returns(Task.CompletedTask);

        await _sut.AddMeasurement(dto);

        _measurementSvc.Verify(s => s.AddMeasurementAsync(It.IsAny<Measurement>()), Times.Once);
    }

    // ── GetMeasurementsByMonitoredId ─────────────────────────────────────────

    [Fact]
    public async Task GetByMonitoredId_Returns400_WhenIdEmpty()
    {
        var result = await _sut.GetMeasurementsByMonitoredId(Guid.Empty);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetByMonitoredId_Returns200_WithData()
    {
        var monitoredId = Guid.NewGuid();
        SetupOwnership(monitoredId);

        var list = new List<MeasurementResponseDTO>
        {
            new() { Pulse = 75, Temperature = 36.8, SpO2 = 98 }
        };
        _measurementSvc.Setup(s => s.GetMeasurementsByMonitoredIdAsync(monitoredId, 1, 10)).ReturnsAsync(list);

        var result = await _sut.GetMeasurementsByMonitoredId(monitoredId, 1, 10);
        result.Should().BeOfType<OkObjectResult>();
    }

    // ── GetMeasurementById ───────────────────────────────────────────────────

    [Fact]
    public async Task GetById_Returns400_WhenIdEmpty()
    {
        var result = await _sut.GetMeasurementById(Guid.Empty);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetById_Returns404_WhenNotFound()
    {
        var id = Guid.NewGuid();
        _measurementSvc.Setup(s => s.GetMeasurementByIdAsync(id))
                       .ReturnsAsync((MeasurementResponseDTO?)null);

        var result = await _sut.GetMeasurementById(id);
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetById_Returns200_WhenFound()
    {
        var id          = Guid.NewGuid();
        var monitoredId = Guid.NewGuid();
        SetupOwnership(monitoredId);

        var dto = new MeasurementResponseDTO { IdMonitored = monitoredId, Pulse = 75, Temperature = 36.8, SpO2 = 98 };
        _measurementSvc.Setup(s => s.GetMeasurementByIdAsync(id)).ReturnsAsync(dto);

        var result = await _sut.GetMeasurementById(id);
        result.Should().BeOfType<OkObjectResult>();
    }

    // ── GetTodayMeasurementsCount ────────────────────────────────────────────

    [Fact]
    public async Task GetTodayCount_Returns200_WithCount()
    {
        _measurementSvc.Setup(s => s.GetTodayMeasurementsCountAsync()).ReturnsAsync(17);

        var result = await _sut.GetTodayMeasurementsCount();
        result.Should().BeOfType<OkObjectResult>();
    }
}
