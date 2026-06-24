using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using LifeAlertPlus.Application.Services;
using LifeAlertPlus.Tests.Helpers;
using Microsoft.Extensions.Configuration;

namespace LifeAlertPlus.Tests.Unit.Application;

// Teste pentru JwtService — generarea token-urilor JWT (HS256) folosite la autentificarea utilizatorilor
public class JwtServiceTests
{
    private readonly JwtService _sut; // SUT = System Under Test

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

        // Un JWT valid are mereu exact 3 segmente separate prin punct: header.payload.semnătură
        token.Split('.').Should().HaveCount(3);
    }

    // Verifică toate claim-urile esențiale folosite de frontend/autorizare (sub, email, nume, rol)
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

    // Dacă Jwt:ExpiresInMinutes nu e configurat, JwtService trebuie să folosească fallback-ul de 60 minute (nu să crape)
    [Fact]
    public void GenerateToken_UsesDefaultExpiry_WhenConfigMissing()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"]      = "super-secret-key-at-least-32-chars!!",
                ["Jwt:Issuer"]   = "LifeAlertPlus",
                ["Jwt:Audience"] = "Client"
                // ExpiresInMinutes omis intenționat → ar trebui să se folosească implicit 60
            })
            .Build();

        var sut   = new JwtService(config);
        var user  = TestDataFactory.CreateUser();
        var token = sut.GenerateToken(user, "User");

        var handler = new JwtSecurityTokenHandler();
        var parsed  = handler.ReadJwtToken(token);

        parsed.ValidTo.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(60), precision: TimeSpan.FromSeconds(30));
    }

    // Fără Jwt:Key, serviciul nu poate semna token-ul (HS256) — trebuie să arunce explicit, nu să producă un JWT nesemnat
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

    // Userii vechi/seed-uiți fără Provider setat trebuie tratați ca "Local" (autentificare email+parolă), nu null în claim
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
