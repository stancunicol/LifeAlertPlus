using FluentAssertions;
using LifeAlertPlus.Application.Services;
using LifeAlertPlus.Shared.DTOs.Requests.User;

namespace LifeAlertPlus.Tests.Unit.Application;

// Teste pentru AuthenticationService — hashing BCrypt, validare complexitate parolă, validare schimbare email/parolă
public class AuthenticationServiceTests
{
    private readonly AuthenticationService _sut = new(); // SUT = System Under Test

    // ── HashPassword ─────────────────────────────────────────────────────────

    [Fact]
    public void HashPassword_ReturnsNonEmptyString()
    {
        var hash = _sut.HashPassword("Test@1234");
        hash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void HashPassword_DoesNotReturnPlainText()
    {
        const string plain = "Test@1234";
        _sut.HashPassword(plain).Should().NotBe(plain);
    }

    [Fact]
    public void HashPassword_ProducesDifferentHashesForSameInput()
    {
        var h1 = _sut.HashPassword("Test@1234");
        var h2 = _sut.HashPassword("Test@1234");
        h1.Should().NotBe(h2); // BCrypt generează un salt diferit la fiecare hash, deci rezultatul diferă chiar pentru aceeași parolă
    }

    // ── VerifyPassword(string, string) ───────────────────────────────────────

    [Fact]
    public void VerifyPassword_Hash_ReturnsTrueForCorrectPassword()
    {
        var hash = _sut.HashPassword("Test@1234");
        _sut.VerifyPassword("Test@1234", hash).Should().BeTrue();
    }

    [Fact]
    public void VerifyPassword_Hash_ReturnsFalseForWrongPassword()
    {
        var hash = _sut.HashPassword("Test@1234");
        _sut.VerifyPassword("Wrong@5678", hash).Should().BeFalse();
    }

    // ── VerifyPassword(string) ───────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task VerifyPassword_String_FailsForNullOrEmpty(string? pw)
    {
        var result = await _sut.VerifyPassword(pw!);
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("required");
    }

    [Fact]
    public async Task VerifyPassword_String_FailsForShortPassword()
    {
        var result = await _sut.VerifyPassword("Ab@1");
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("8 characters");
    }

    [Fact]
    public async Task VerifyPassword_String_FailsForAllLowercase()
    {
        // Fără cifre, fără caractere speciale, doar litere mici → condiția All(char.IsLower) e adevărată, deci validarea trebuie să respingă
        var result = await _sut.VerifyPassword("alllower");
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyPassword_String_FailsForNoSpecialChar()
    {
        var result = await _sut.VerifyPassword("NoSpecial1234");
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyPassword_String_FailsForNoDigit()
    {
        var result = await _sut.VerifyPassword("NoDigit!@#ABC");
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyPassword_String_SucceedsForValidPassword()
    {
        var result = await _sut.VerifyPassword("Valid@1234");
        result.Success.Should().BeTrue();
    }

    // ── ValidateChangePassword ───────────────────────────────────────────────

    [Theory]
    [InlineData(null, "New@1234", "New@1234")]
    [InlineData("Old@1234", null, "New@1234")]
    [InlineData("Old@1234", "New@1234", null)]
    public async Task ValidateChangePassword_FailsWhenFieldsMissing(string? current, string? newPw, string? confirm)
    {
        var result = await _sut.ValidateChangePassword(current, newPw, confirm);
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateChangePassword_FailsWhenPasswordsMismatch()
    {
        var result = await _sut.ValidateChangePassword("Old@1234", "New@1234", "Different@1234");
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("match");
    }

    // Parola nouă identică cu cea curentă e respinsă explicit — previne o "schimbare" care nu schimbă nimic
    [Fact]
    public async Task ValidateChangePassword_FailsWhenNewSameAsCurrent()
    {
        var result = await _sut.ValidateChangePassword("Same@1234", "Same@1234", "Same@1234");
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("same");
    }

    [Fact]
    public async Task ValidateChangePassword_SucceedsForValidInput()
    {
        var result = await _sut.ValidateChangePassword("Old@1234", "NewValid@9876", "NewValid@9876");
        result.Success.Should().BeTrue();
    }

    // ── ValidateChangeEmail ──────────────────────────────────────────────────

    [Fact]
    public async Task ValidateChangeEmail_FailsWhenCurrentEmailMissing()
    {
        var req = new UserChangeEmailRequestDTO
        {
            CurrentEmail  = "",
            NewEmail      = "new@example.com",
            ConfirmEmail  = "new@example.com",
            CurrentPassword = "Pass@1234"
        };
        var result = await _sut.ValidateChangeEmail(req);
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateChangeEmail_FailsWhenNewAndConfirmMismatch()
    {
        var req = new UserChangeEmailRequestDTO
        {
            CurrentEmail    = "old@example.com",
            NewEmail        = "new@example.com",
            ConfirmEmail    = "other@example.com",
            CurrentPassword = "Pass@1234"
        };
        var result = await _sut.ValidateChangeEmail(req);
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateChangeEmail_FailsWhenNewEmailSameAsCurrent()
    {
        var req = new UserChangeEmailRequestDTO
        {
            CurrentEmail    = "same@example.com",
            NewEmail        = "same@example.com",
            ConfirmEmail    = "same@example.com",
            CurrentPassword = "Pass@1234"
        };
        var result = await _sut.ValidateChangeEmail(req);
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateChangeEmail_FailsForInvalidEmailFormat()
    {
        var req = new UserChangeEmailRequestDTO
        {
            CurrentEmail    = "old@example.com",
            NewEmail        = "not-an-email",
            ConfirmEmail    = "not-an-email",
            CurrentPassword = "Pass@1234"
        };
        var result = await _sut.ValidateChangeEmail(req);
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Invalid email");
    }

    [Fact]
    public async Task ValidateChangeEmail_SucceedsForValidInput()
    {
        var req = new UserChangeEmailRequestDTO
        {
            CurrentEmail    = "old@example.com",
            NewEmail        = "new@example.com",
            ConfirmEmail    = "new@example.com",
            CurrentPassword = "Pass@1234"
        };
        var result = await _sut.ValidateChangeEmail(req);
        result.Success.Should().BeTrue();
    }
}
