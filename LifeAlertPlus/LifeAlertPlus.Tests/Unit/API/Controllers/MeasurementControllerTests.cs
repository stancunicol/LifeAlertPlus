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
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace LifeAlertPlus.Tests.Unit.API.Controllers;

public class MeasurementControllerTests
{
    private readonly Mock<IMeasurementService>   _measurementSvc   = new();
    private readonly Mock<IUserMonitoredService> _userMonitoredSvc = new();
    private readonly Guid                        _callerId         = Guid.NewGuid();
    private readonly MeasurementController       _sut;

    public MeasurementControllerTests()
    {
        _sut = new MeasurementController(_measurementSvc.Object, BuildAlertMonitor(), _userMonitoredSvc.Object);

        // Provide an authenticated user so UserOwnsMonitoredAsync can read the caller id
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, _callerId.ToString()) };
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
            }
        };
    }

    // Makes _callerId the owner of the given monitored person
    private void SetupOwnership(Guid monitoredId)
    {
        _userMonitoredSvc
            .Setup(s => s.GetMonitoredPeopleByUserIdAsync(_callerId))
            .ReturnsAsync([new Monitored { Id = monitoredId }]);
    }

    private static AlertMonitorService BuildAlertMonitor()
    {
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Throws<InvalidOperationException>();

        var conditionEngine    = new ConditionRuleEngine(scopeFactory.Object, TestDataFactory.CreateLogger<ConditionRuleEngine>());
        var activityProfileSvc = new ActivityProfileService(scopeFactory.Object, TestDataFactory.CreateLogger<ActivityProfileService>());
        var nearestHospitalSvc = new NearestHospitalService(TestDataFactory.CreateLogger<NearestHospitalService>());

        return new AlertMonitorService(
            scopeFactory.Object,
            TestDataFactory.CreateLogger<AlertMonitorService>(),
            TestDataFactory.CreateAlertMonitorConfiguration(),
            Mock.Of<IPushNotificationService>(),
            activityProfileSvc,
            conditionEngine,
            nearestHospitalSvc);
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
