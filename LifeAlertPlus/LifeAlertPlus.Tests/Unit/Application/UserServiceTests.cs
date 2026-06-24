using FluentAssertions;
using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Application.Services;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Shared.DTOs.Requests.User;
using LifeAlertPlus.Tests.Helpers;
using Moq;

namespace LifeAlertPlus.Tests.Unit.Application;

// Teste pentru UserService — generare token-uri, înregistrare, schimbare parolă/email, OAuth Google
public class UserServiceTests
{
    private readonly Mock<IUserRepository>        _userRepo  = new();
    private readonly Mock<IAuthenticationService> _authSvc   = new();
    private readonly Mock<IRoleRepository>        _roleRepo  = new();
    private readonly UserService                  _sut; // SUT = System Under Test

    public UserServiceTests()
    {
        _sut = new UserService(_userRepo.Object, _authSvc.Object, _roleRepo.Object);
    }

    // ── Token generation ─────────────────────────────────────────────────────

    [Fact]
    public void GenerateEmailVerificationToken_ReturnsNonEmptyString()
    {
        _sut.GenerateEmailVerificationToken().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GenerateEmailVerificationToken_IsUnique()
    {
        var t1 = _sut.GenerateEmailVerificationToken();
        var t2 = _sut.GenerateEmailVerificationToken();
        t1.Should().NotBe(t2);
    }

    [Fact]
    public void GeneratePasswordResetToken_ReturnsNonEmptyString()
    {
        _sut.GeneratePasswordResetToken().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GenerateEmailChangeCancelToken_ReturnsNonEmptyString()
    {
        _sut.GenerateEmailChangeCancelToken().Should().NotBeNullOrEmpty();
    }

    // ── VerifyEmailAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task VerifyEmailAsync_ReturnsNull_WhenTokenNotFound()
    {
        _userRepo.Setup(r => r.GetUserByEmailConfirmationTokenAsync(It.IsAny<string>()))
                 .ReturnsAsync((User?)null);

        var result = await _sut.VerifyEmailAsync("invalid-token");
        result.Should().BeNull();
    }

    [Fact]
    public async Task VerifyEmailAsync_ReturnsNull_WhenTokenExpired()
    {
        var user = TestDataFactory.CreateUser();
        user.EmailConfirmationToken   = "some-token";
        user.EmailConfirmationExpires = DateTime.UtcNow.AddHours(-1); // expirat (în trecut)

        _userRepo.Setup(r => r.GetUserByEmailConfirmationTokenAsync("some-token"))
                 .ReturnsAsync(user);

        var result = await _sut.VerifyEmailAsync("some-token");
        result.Should().BeNull();
    }

    [Fact]
    public async Task VerifyEmailAsync_ConfirmsEmail_WhenTokenValid()
    {
        var user = TestDataFactory.CreateUser();
        user.IsEmailConfirmed         = false;
        user.EmailConfirmationToken   = "valid-token";
        user.EmailConfirmationExpires = DateTime.UtcNow.AddHours(1);

        _userRepo.Setup(r => r.GetUserByEmailConfirmationTokenAsync("valid-token"))
                 .ReturnsAsync(user);
        _userRepo.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);

        var result = await _sut.VerifyEmailAsync("valid-token");

        result.Should().NotBeNull();
        result!.IsEmailConfirmed.Should().BeTrue();
        result.EmailConfirmationToken.Should().BeNull();
        result.EmailConfirmationExpires.Should().BeNull();
    }

    // ── CreateUserAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateUserAsync_ThrowsInvalidOperation_WhenUserRoleMissing()
    {
        _roleRepo.Setup(r => r.GetRoleByNameAsync("User")).ReturnsAsync((Role?)null);

        var act = async () => await _sut.CreateUserAsync(new UserRegisterRequestDTO
        {
            FirstName = "Test", LastName = "User", Email = "t@t.com", Password = "Pass@1234"
        });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Default role*");
    }

    [Fact]
    public async Task CreateUserAsync_CreatesUserWithCorrectDefaults()
    {
        _roleRepo.Setup(r => r.GetRoleByNameAsync("User"))
                 .ReturnsAsync(TestDataFactory.CreateUserRole());
        _authSvc.Setup(a => a.HashPassword(It.IsAny<string>())).Returns("hashed");
        _userRepo.Setup(r => r.CreateUserAsync(It.IsAny<User>())).ReturnsAsync(true);

        var created = await _sut.CreateUserAsync(new UserRegisterRequestDTO
        {
            FirstName = "Ana", LastName = "Pop", Email = "ana@test.com", Password = "Ana@1234"
        });

        created.Should().BeTrue();
        _userRepo.Verify(r => r.CreateUserAsync(It.Is<User>(u =>
            u.Email == "ana@test.com" &&
            u.FirstName == "Ana" &&
            !u.IsEmailConfirmed &&
            u.EmailConfirmationToken != null &&
            u.Provider == "Local" &&
            u.NotifyByEmail &&
            u.MinHeartRate == 60
        )), Times.Once);
    }

    // ── InitiatePasswordResetAsync ───────────────────────────────────────────

    [Fact]
    public async Task InitiatePasswordResetAsync_SetsTokenAndExpiry()
    {
        var user = TestDataFactory.CreateUser();
        _userRepo.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);

        await _sut.InitiatePasswordResetAsync(user);

        user.PasswordResetToken.Should().NotBeNullOrEmpty();
        user.PasswordResetExpires.Should().BeCloseTo(DateTime.UtcNow.AddHours(1), precision: TimeSpan.FromSeconds(30));
    }

    // ── PasswordChangeAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task PasswordChangeAsync_HashesAndClearsResetToken()
    {
        var user = TestDataFactory.CreateUser();
        user.PasswordResetToken   = "old-reset-token";
        user.PasswordResetExpires = DateTime.UtcNow.AddHours(1);

        _authSvc.Setup(a => a.HashPassword("New@1234")).Returns("new-hash");
        _userRepo.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);

        await _sut.PasswordChangeAsync(user, "New@1234");

        user.PasswordHash.Should().Be("new-hash");
        user.PasswordResetToken.Should().BeNull();
        user.PasswordResetExpires.Should().BeNull();
        user.LastChangedPasswordAt.Should().NotBeNull();
    }

    // ── EmailChangeAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task EmailChangeAsync_UpdatesEmailAndClearsTokens()
    {
        var user = TestDataFactory.CreateUser();
        user.PendingEmail           = "new@example.com";
        user.EmailChangeToken       = "change-token";
        user.EmailChangeCancelToken = "cancel-token";
        user.EmailChangeExpires     = DateTime.UtcNow.AddHours(1);

        _userRepo.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);

        await _sut.EmailChangeAsync(user);

        user.Email.Should().Be("new@example.com");
        user.IsEmailConfirmed.Should().BeTrue();
        user.PendingEmail.Should().BeNull();
        user.EmailChangeToken.Should().BeNull();
        user.EmailChangeCancelToken.Should().BeNull();
    }

    [Fact]
    public async Task EmailChangeAsync_DoesNothing_WhenNoPendingEmail()
    {
        var user = TestDataFactory.CreateUser();
        user.PendingEmail = null;

        _userRepo.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);

        await _sut.EmailChangeAsync(user);

        _userRepo.Verify(r => r.UpdateUserAsync(It.IsAny<User>()), Times.Never);
    }

    // ── CancelEmailChangeAsync ───────────────────────────────────────────────

    [Fact]
    public async Task CancelEmailChangeAsync_ClearsAllEmailChangeFields()
    {
        var user = TestDataFactory.CreateUser();
        user.PendingEmail           = "pending@example.com";
        user.EmailChangeToken       = "tok";
        user.EmailChangeCancelToken = "cancel";
        user.EmailChangeExpires     = DateTime.UtcNow.AddHours(1);

        _userRepo.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);

        await _sut.CancelEmailChangeAsync(user);

        user.PendingEmail.Should().BeNull();
        user.EmailChangeToken.Should().BeNull();
        user.EmailChangeCancelToken.Should().BeNull();
        user.EmailChangeExpires.Should().BeNull();
    }

    // ── FindOrCreateGoogleUserAsync ──────────────────────────────────────────

    [Fact]
    public async Task FindOrCreateGoogleUserAsync_CreatesNewUser_WhenNotExists()
    {
        _roleRepo.Setup(r => r.GetRoleByNameAsync("User"))
                 .ReturnsAsync(TestDataFactory.CreateUserRole());
        _userRepo.Setup(r => r.GetUserByEmailAsync("google@test.com"))
                 .ReturnsAsync((User?)null);
        _userRepo.Setup(r => r.CreateUserAsync(It.IsAny<User>())).ReturnsAsync(true);

        var result = await _sut.FindOrCreateGoogleUserAsync(
            "google@test.com", "Google User", "google-id-123",
            "Google", "User", "https://pic.url");

        result.Should().NotBeNull();
        result!.Email.Should().Be("google@test.com");
        result.Provider.Should().Be("Google");
        result.IsEmailConfirmed.Should().BeTrue();
    }

    // Login Google repetat cu același cont existent — nu se creează un al doilea cont, se actualizează profilul existent
    [Fact]
    public async Task FindOrCreateGoogleUserAsync_ReturnsExistingUser_WhenAlreadyExists()
    {
        var existing = TestDataFactory.CreateUser(email: "google@test.com");
        existing.Provider    = "Google";
        existing.ProviderKey = "google-id-123";

        _roleRepo.Setup(r => r.GetRoleByNameAsync("User"))
                 .ReturnsAsync(TestDataFactory.CreateUserRole());
        _userRepo.Setup(r => r.GetUserByEmailAsync("google@test.com"))
                 .ReturnsAsync(existing);
        _userRepo.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);

        var result = await _sut.FindOrCreateGoogleUserAsync(
            "google@test.com", "Google User", "google-id-123",
            "Google", "User", null);

        result.Should().NotBeNull();
        result!.Id.Should().Be(existing.Id);
    }
}
