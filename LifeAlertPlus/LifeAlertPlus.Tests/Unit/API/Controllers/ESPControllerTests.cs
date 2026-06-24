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

// Teste pentru ESPController — endpoint-urile publice apelate de dispozitivele ESP32 (ingest date, panic, WiFi config, heartbeat)
// Autentificarea NU e JWT, ci o cheie HMAC-SHA256 calculată din seria dispozitivului + un secret partajat (Urls:EspDeviceSecret),
// trimisă în header-ul X-Device-Key. Asta permite ESP32-ului (care nu poate gestiona login/parolă) să se autentifice simplu.
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

    // Calculează cheia HMAC-SHA256 validă pentru un serial — reproduce exact algoritmul folosit de firmware-ul ESP32 și de ESPController
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

    // O cheie HMAC validă, dar calculată pentru ALT serial, nu trebuie acceptată — previne ca un dispozitiv compromis
    // să trimită date în numele altui dispozitiv (cheia e legată criptografic de serialul exact)
    [Fact]
    public async Task Ingest_Returns401_WhenKeyIsForDifferentSerial()
    {
        SetDeviceKeyHeader(ValidKey("ESP32-FFFFFFFF"));
        var result = await _sut.IngestESPData(new ESPDataResponseDTO { Serial = Serial });
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    // Rate limiting per dispozitiv (IsIngestAllowed) — previne flood-ul de date de la un ESP32 defect/compromis
    [Fact]
    public async Task Ingest_Returns429_WhenRateLimited()
    {
        SetValidKey();
        _alertSvc.Setup(a => a.IsIngestAllowed(Serial)).Returns(false);
        var result = await _sut.IngestESPData(new ESPDataResponseDTO { Serial = Serial });
        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(429);
    }

    // Un dispozitiv neasociat încă cu niciun pacient trebuie să primească 200 (nu eroare) — ESP32-ul poate fi pornit
    // înainte de a fi atribuit unui pacient în aplicație; pur și simplu nu salvăm nimic
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

    // Pacient arhivat (monitorizare oprită) — datele nu mai sunt salvate, dar ESP32-ul nu primește o eroare care l-ar face să reîncerce
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

    // Format alternativ de payload: senzorul MAX30100 trimite [puls, SpO2] ca listă brută în loc de câmpuri separate Bpm/Spo2 —
    // controller-ul trebuie să normalizeze ambele formate la aceeași entitate Measurement
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

    // ── Panic (buton de urgență fizic pe dispozitiv) ─────────────────────────────────────────────────────────────────

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

    // La Panic (diferit de Ingest), un dispozitiv neasociat e o eroare reală (404) — un buton de urgență apăsat
    // pe un dispozitiv neconfigurat e o situație care trebuie semnalată, nu ignorată silențios
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

    // ── WifiConfig (cerut de ESP32 la pornire — rețele salvate + frecvența de actualizare) ────────────────────────────────────────────────────────────

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

    // Fără pacient asociat, frecvența de actualizare implicită e 30 secunde (30000ms) — valoare de siguranță rezonabilă
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

    // ── Heartbeat (semnal periodic "sunt online", include puterea semnalului WiFi RSSI) ─────────────────────────────────────────────────────────────

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
