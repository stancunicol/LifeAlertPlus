using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using LifeAlertPlus.Application.Services;
using LifeAlertPlus.Tests.Helpers;
using Microsoft.Extensions.Configuration;

namespace LifeAlertPlus.Tests.Unit.Application;

public class JwtServiceTests
{
    private readonly JwtService _sut;

    public JwtServiceTests()
    {
        _sut = new JwtService(TestDataFactory.CreateJwtConfiguration());
    }

    [Fact]
    public void GenerateToken_ReturnsNonEmptyString()
    {
        var user = TestDataFactory.CreateUser();
        var token = _sut.GenerateToken(user, "User");
        token.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GenerateToken_ReturnsValidJwtFormat()
    {
        var user  = TestDataFactory.CreateUser();
        var token = _sut.GenerateToken(user, "User");

        // A JWT always has exactly two dots
        token.Split('.').Should().HaveCount(3);
    }

    [Fact]
    public void GenerateToken_ContainsExpectedClaims()
    {
        var user  = TestDataFactory.CreateUser();
        var token = _sut.GenerateToken(user, "Admin");

        var handler    = new JwtSecurityTokenHandler();
        var parsed     = handler.ReadJwtToken(token);
        var claimMap   = parsed.Claims.ToDictionary(c => c.Type, c => c.Value);

        claimMap[JwtRegisteredClaimNames.Sub].Should().Be(user.Id.ToString());
        claimMap[JwtRegisteredClaimNames.Email].Should().Be(user.Email);
        claimMap["firstName"].Should().Be(user.FirstName);
        claimMap["lastName"].Should().Be(user.LastName);
        claimMap["role"].Should().Be("Admin");
    }

    [Fact]
    public void GenerateToken_UsesDefaultExpiry_WhenConfigMissing()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"]      = "super-secret-key-at-least-32-chars!!",
                ["Jwt:Issuer"]   = "LifeAlertPlus",
                ["Jwt:Audience"] = "Client"
                // ExpiresInMinutes intentionally omitted → should default to 60
            })
            .Build();

        var sut   = new JwtService(config);
        var user  = TestDataFactory.CreateUser();
        var token = sut.GenerateToken(user, "User");

        var handler = new JwtSecurityTokenHandler();
        var parsed  = handler.ReadJwtToken(token);

        parsed.ValidTo.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(60), precision: TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void GenerateToken_ThrowsWhenJwtKeyMissing()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"]   = "LifeAlertPlus",
                ["Jwt:Audience"] = "Client"
            })
            .Build();

        var sut  = new JwtService(config);
        var user = TestDataFactory.CreateUser();

        var act = () => sut.GenerateToken(user, "User");
        act.Should().Throw<InvalidOperationException>().WithMessage("*Jwt:Key*");
    }

    [Fact]
    public void GenerateToken_ProviderClaimDefaultsToLocal_WhenUserProviderIsNull()
    {
        var user = TestDataFactory.CreateUser();
        user.Provider = null;

        var token  = _sut.GenerateToken(user, "User");
        var parsed = new JwtSecurityTokenHandler().ReadJwtToken(token);

        parsed.Claims.First(c => c.Type == "provider").Value.Should().Be("Local");
    }
}
