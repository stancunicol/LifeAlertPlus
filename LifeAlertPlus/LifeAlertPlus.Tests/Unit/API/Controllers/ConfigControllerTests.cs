using FluentAssertions;
using LifeAlertPlus.API.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace LifeAlertPlus.Tests.Unit.API.Controllers;

// Teste pentru ConfigController — endpoint-ul prin care frontend-ul Blazor cere cheia Google Maps API
// (cheia nu e expusă direct în clientul WASM, trece prin backend ca să nu fie vizibilă în codul JS static)
public class ConfigControllerTests
{
    // Construiește controller-ul cu o configurație în memorie (fără appsettings.json real) — mapsKey null simulează cheia neconfigurată
    private static ConfigController BuildSut(string? mapsKey)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(mapsKey is null
                ? []
                : new Dictionary<string, string?> { ["GoogleMaps:ApiKey"] = mapsKey })
            .Build();

        return new ConfigController(config);
    }

    [Fact]
    public void GetMapsKey_WhenKeyIsConfigured_ReturnsOkWithApiKey()
    {
        var sut = BuildSut("test-api-key-123");

        var result = sut.GetMapsKey();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(new { apiKey = "test-api-key-123" });
    }

    // Cheia lipsă, goală sau doar spații trebuie tratate identic — 404, ca frontend-ul să știe explicit că harta nu e disponibilă
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetMapsKey_WhenKeyIsMissingOrBlank_ReturnsNotFound(string? key)
    {
        var sut = BuildSut(key);

        var result = sut.GetMapsKey();

        result.Should().BeOfType<NotFoundObjectResult>();
    }
}
