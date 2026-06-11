using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using LifeAlertPlus.API.Controllers;
using LifeAlertPlus.API.Services;
using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Shared.DTOs.Requests.ESP;
using LifeAlertPlus.Shared.DTOs.Responses.ESP;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LifeAlertPlus.Tests.Unit.API.Controllers;

public class ESPControllerTests
{
    private const string Secret = "test-secret";
    private const string Serial = "ESP32-AABBCCDDEE";

    private readonly Mock<IConfiguration> _config = new();
    private readonly Mock<ISimulationManager> _sim = new();
    private readonly Mock<IAlertMonitorService> _alertSvc = new();
    private readonly Mock<IMonitoredService> _monitoredSvc = new();
    private readonly Mock<IUserMonitoredService> _userMonitoredSvc = new();
    private readonly Mock<IMeasurementService> _measurementSvc = new();
    private readonly Mock<IWifiNetworkService> _wifiSvc = new();
    private readonly Mock<IDeviceTestLogService> _logSvc = new();

    private readonly ESPController _sut;

    public ESPControllerTests()
    {
        _config.Setup(c => c["Urls:EspDeviceSecret"]).Returns(Secret);
        _alertSvc.Setup(a => a.IsIngestAllowed(It.IsAny<string>())).Returns(true);

        _sut = new ESPController(
            _config.Object,
            NullLogger<ESPController>.Instance,
            _sim.Object,
            _alertSvc.Object,
            _monitoredSvc.Object,
            _userMonitoredSvc.Object,
            _measurementSvc.Object,
            _wifiSvc.Object,
            _logSvc.Object);
    }

    private static string ValidKey(string serial = Serial)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(serial))).ToLowerInvariant();
    }

    private void SetDeviceKeyHeader(string key)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Device-Key"] = key;
        _sut.ControllerContext = new ControllerContext { HttpContext = ctx };
    }

    private void SetValidKey(string serial = Serial) => SetDeviceKeyHeader(ValidKey(serial));

    // ── Ingest ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ingest_Returns400_WhenSerialMissing()
    {
        SetValidKey();
        var result = await _sut.IngestESPData(new ESPDataResponseDTO { Serial = "" });
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Ingest_Returns401_WhenKeyMissing()
    {
        SetDeviceKeyHeader("");
        var result = await _sut.IngestESPData(new ESPDataResponseDTO { Serial = Serial });
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Ingest_Returns401_WhenKeyWrong()
    {
        SetDeviceKeyHeader("wrong-key");
        var result = await _sut.IngestESPData(new ESPDataResponseDTO { Serial = Serial });
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Ingest_Returns401_WhenKeyIsForDifferentSerial()
    {
        SetDeviceKeyHeader(ValidKey("ESP32-FFFFFFFF"));
        var result = await _sut.IngestESPData(new ESPDataResponseDTO { Serial = Serial });
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Ingest_Returns429_WhenRateLimited()
    {
        SetValidKey();
        _alertSvc.Setup(a => a.IsIngestAllowed(Serial)).Returns(false);
        var result = await _sut.IngestESPData(new ESPDataResponseDTO { Serial = Serial });
        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(429);
    }

    [Fact]
    public async Task Ingest_Returns200_WhenNoMonitoredLinked()
    {
        SetValidKey();
        _monitoredSvc.Setup(m => m.GetMonitoredPersonByDeviceSerialNumberAsync(Serial))
                     .ReturnsAsync((Monitored?)null);

        var result = await _sut.IngestESPData(new ESPDataResponseDTO { Serial = Serial, Date = 1 });

        result.Should().BeOfType<OkResult>();
        _measurementSvc.Verify(m => m.AddMeasurementAsync(It.IsAny<Measurement>()), Times.Never);
    }

    [Fact]
    public async Task Ingest_Returns200_WhenMonitoredArchived()
    {
        SetValidKey();
        _monitoredSvc.Setup(m => m.GetMonitoredPersonByDeviceSerialNumberAsync(Serial))
                     .ReturnsAsync(new Monitored { IsArchived = true });

        var result = await _sut.IngestESPData(new ESPDataResponseDTO { Serial = Serial, Date = 1 });

        result.Should().BeOfType<OkObjectResult>();
        _measurementSvc.Verify(m => m.AddMeasurementAsync(It.IsAny<Measurement>()), Times.Never);
    }

    [Fact]
    public async Task Ingest_SavesMeasurement_WhenMonitoredActive()
    {
        SetValidKey();
        var monitored = new Monitored { Id = Guid.NewGuid(), IsArchived = false };
        _monitoredSvc.Setup(m => m.GetMonitoredPersonByDeviceSerialNumberAsync(Serial))
                     .ReturnsAsync(monitored);

        var result = await _sut.IngestESPData(new ESPDataResponseDTO
        {
            Serial = Serial, Date = 1, Bpm = 72, Spo2 = 98, Temperature = 36.5
        });

        result.Should().BeOfType<OkResult>();
        _measurementSvc.Verify(m => m.AddMeasurementAsync(
            It.Is<Measurement>(x => x.IdMonitored == monitored.Id && x.Pulse == 72 && x.SpO2 == 98)),
            Times.Once);
    }

    [Fact]
    public async Task Ingest_NormalizesBpmSpo2FromMax30100()
    {
        SetValidKey();
        var monitored = new Monitored { Id = Guid.NewGuid(), IsArchived = false };
        _monitoredSvc.Setup(m => m.GetMonitoredPersonByDeviceSerialNumberAsync(Serial))
                     .ReturnsAsync(monitored);

        await _sut.IngestESPData(new ESPDataResponseDTO
        {
            Serial = Serial, Date = 1,
            Max30100 = new List<int> { 75, 97 }
        });

        _measurementSvc.Verify(m => m.AddMeasurementAsync(
            It.Is<Measurement>(x => x.Pulse == 75 && x.SpO2 == 97)),
            Times.Once);
    }

    // ── Panic ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Panic_Returns400_WhenSerialMissing()
    {
        SetValidKey();
        var result = await _sut.PanicAlert(new ESPPanicDTO { Serial = "" });
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Panic_Returns401_WhenKeyWrong()
    {
        SetDeviceKeyHeader("bad");
        var result = await _sut.PanicAlert(new ESPPanicDTO { Serial = Serial });
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Panic_Returns404_WhenMonitoredNotFound()
    {
        SetValidKey();
        _monitoredSvc.Setup(m => m.GetMonitoredPersonByDeviceSerialNumberAsync(Serial))
                     .ReturnsAsync((Monitored?)null);

        var result = await _sut.PanicAlert(new ESPPanicDTO { Serial = Serial });

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Panic_Returns200_WhenMonitoredArchived()
    {
        SetValidKey();
        _monitoredSvc.Setup(m => m.GetMonitoredPersonByDeviceSerialNumberAsync(Serial))
                     .ReturnsAsync(new Monitored { IsArchived = true });

        var result = await _sut.PanicAlert(new ESPPanicDTO { Serial = Serial });

        result.Should().BeOfType<OkObjectResult>();
        _alertSvc.Verify(a => a.TriggerPanicAlertAsync(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Panic_TriggersPanicAlert_WhenValid()
    {
        SetValidKey();
        var monitored = new Monitored { Id = Guid.NewGuid(), IsArchived = false };
        _monitoredSvc.Setup(m => m.GetMonitoredPersonByDeviceSerialNumberAsync(Serial))
                     .ReturnsAsync(monitored);

        var result = await _sut.PanicAlert(new ESPPanicDTO { Serial = Serial, Coordinates = "44.0,26.0" });

        result.Should().BeOfType<OkResult>();
        _alertSvc.Verify(a => a.TriggerPanicAlertAsync(monitored.Id, "44.0,26.0"), Times.Once);
    }

    // ── WifiConfig ────────────────────────────────────────────────────────────

    [Fact]
    public async Task WifiConfig_Returns400_WhenSerialMissing()
    {
        SetValidKey();
        var result = await _sut.GetWifiConfig("  ");
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task WifiConfig_Returns401_WhenKeyWrong()
    {
        SetDeviceKeyHeader("bad");
        var result = await _sut.GetWifiConfig(Serial);
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task WifiConfig_ReturnsDefault30s_WhenNoMonitored()
    {
        SetValidKey();
        _wifiSvc.Setup(w => w.GetByDeviceSerialAsync(Serial)).ReturnsAsync(Array.Empty<WifiNetwork>());
        _monitoredSvc.Setup(m => m.GetMonitoredPersonByDeviceSerialNumberAsync(Serial))
                     .ReturnsAsync((Monitored?)null);

        var result = await _sut.GetWifiConfig(Serial);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        json.Should().Contain("\"updateIntervalMs\":30000");
    }

    [Fact]
    public async Task WifiConfig_ReturnsMonitoredFrequency_WhenSet()
    {
        SetValidKey();
        _wifiSvc.Setup(w => w.GetByDeviceSerialAsync(Serial)).ReturnsAsync(Array.Empty<WifiNetwork>());
        _monitoredSvc.Setup(m => m.GetMonitoredPersonByDeviceSerialNumberAsync(Serial))
                     .ReturnsAsync(new Monitored { UpdateFrequency = 60 });

        var result = await _sut.GetWifiConfig(Serial);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        json.Should().Contain("\"updateIntervalMs\":60000");
    }

    [Fact]
    public async Task WifiConfig_ReturnsNetworks()
    {
        SetValidKey();
        _wifiSvc.Setup(w => w.GetByDeviceSerialAsync(Serial)).ReturnsAsync(new[]
        {
            new WifiNetwork { Ssid = "HomeNet", Password = "pass123" }
        });
        _monitoredSvc.Setup(m => m.GetMonitoredPersonByDeviceSerialNumberAsync(Serial))
                     .ReturnsAsync((Monitored?)null);

        var result = await _sut.GetWifiConfig(Serial);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        json.Should().Contain("HomeNet");
    }

    // ── Heartbeat ─────────────────────────────────────────────────────────────

    [Fact]
    public void Heartbeat_Returns400_WhenSerialMissing()
    {
        SetValidKey();
        var result = _sut.Heartbeat(new ESPHeartbeatDTO { Serial = "" });
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void Heartbeat_Returns401_WhenKeyWrong()
    {
        SetDeviceKeyHeader("bad");
        var result = _sut.Heartbeat(new ESPHeartbeatDTO { Serial = Serial });
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public void Heartbeat_Returns200_AndStoresHeartbeat_WhenValid()
    {
        SetValidKey();
        var payload = new ESPHeartbeatDTO { Serial = Serial, RssiDbm = -60 };

        var result = _sut.Heartbeat(payload);

        result.Should().BeOfType<OkResult>();
        _sim.Verify(s => s.SetHeartbeat(Serial, payload), Times.Once);
    }
}
