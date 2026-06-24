using FluentAssertions;
using LifeAlertPlus.API.Controllers;
using LifeAlertPlus.API.Services;
using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Shared.DTOs.Requests.User;
using LifeAlertPlus.Shared.DTOs.Responses.User;
using LifeAlertPlus.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace LifeAlertPlus.Tests.Unit.API.Controllers;

// Teste pentru AuthenticationController — Login, Register, ForgotPassword, ResetPassword
// Atenție specială la testele care verifică prevenirea enumerării email-urilor (vezi ForgotPassword)
public class AuthenticationControllerTests
{
    private readonly Mock<IUserService>           _userSvc   = new();
    private readonly Mock<IAuthenticationService> _authSvc   = new();
    private readonly Mock<IJwtService>            _jwtSvc    = new();
    private readonly Mock<IEmailService>          _emailSvc  = new();
    private readonly Mock<IRoleService>           _roleSvc   = new();
    private readonly AuthenticationController     _sut; // SUT = System Under Test

    public AuthenticationControllerTests()
    {
        var config      = TestDataFactory.CreateJwtConfiguration();
        var logger      = Mock.Of<ILogger<AuthenticationController>>();
        var httpContext = new Mock<IHttpContextAccessor>();
        httpContext.Setup(h => h.HttpContext).Returns(new DefaultHttpContext());

        var getUrlSvc = new GetUrlService(config, httpContext.Object);

        var scopeFactory = new Mock<IServiceScopeFactory>().Object;
        var auditLogger  = Mock.Of<ILogger<LifeAlertPlus.API.Services.AuditService>>();
        var auditSvc     = new LifeAlertPlus.API.Services.AuditService(scopeFactory, auditLogger);

        _sut = new AuthenticationController(
            _userSvc.Object, config, _authSvc.Object, _jwtSvc.Object,
            _emailSvc.Object, logger, getUrlSvc, _roleSvc.Object, auditSvc);
    }

    // ── Login ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_Returns401_WhenUserNotFound()
    {
        _userSvc.Setup(s => s.GetUserByEmailAsync("unknown@test.com")).ReturnsAsync((User?)null);

        var result = await _sut.Login(new UserLoginRequestDTO { Email = "unknown@test.com", Password = "Pass@1234" });

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Login_Returns401_WhenPasswordInvalid()
    {
        var user = TestDataFactory.CreateUser(email: "user@test.com");
        _userSvc.Setup(s => s.GetUserByEmailAsync("user@test.com")).ReturnsAsync(user);
        _authSvc.Setup(a => a.VerifyPassword("WrongPass", user.PasswordHash!)).Returns(false);

        var result = await _sut.Login(new UserLoginRequestDTO { Email = "user@test.com", Password = "WrongPass" });

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Login_Returns401_WhenEmailNotConfirmed()
    {
        var user = TestDataFactory.CreateUser(email: "user@test.com", emailConfirmed: false);
        _userSvc.Setup(s => s.GetUserByEmailAsync("user@test.com")).ReturnsAsync(user);
        _authSvc.Setup(a => a.VerifyPassword("Pass@1234", user.PasswordHash!)).Returns(true);

        var result = await _sut.Login(new UserLoginRequestDTO { Email = "user@test.com", Password = "Pass@1234" });

        result.Should().BeOfType<UnauthorizedObjectResult>();
        var body = ((UnauthorizedObjectResult)result).Value as UserLoginResponseDTO;
        body!.Message.Should().Contain("verify");
    }

    [Fact]
    public async Task Login_Returns200_WithToken_WhenCredentialsValid()
    {
        var user = TestDataFactory.CreateUser(email: "user@test.com");
        var role = TestDataFactory.CreateUserRole();

        _userSvc.Setup(s => s.GetUserByEmailAsync("user@test.com")).ReturnsAsync(user);
        _authSvc.Setup(a => a.VerifyPassword("Pass@1234", user.PasswordHash!)).Returns(true);
        _roleSvc.Setup(r => r.GetByIdAsync(user.RoleId)).ReturnsAsync(role);
        _jwtSvc.Setup(j => j.GenerateToken(user, "User")).Returns("jwt-token");

        var result = await _sut.Login(new UserLoginRequestDTO { Email = "user@test.com", Password = "Pass@1234" });

        result.Should().BeOfType<OkObjectResult>();
        var body = ((OkObjectResult)result).Value as UserLoginResponseDTO;
        body!.Success.Should().BeTrue();
        body.Token.Should().Be("jwt-token");
    }

    // ── Register ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Register_Returns409_WhenEmailAlreadyUsed()
    {
        var existing = TestDataFactory.CreateUser(email: "taken@test.com");
        _userSvc.Setup(s => s.GetUserByEmailAsync("taken@test.com")).ReturnsAsync(existing);

        var result = await _sut.Register(new UserRegisterRequestDTO
        {
            FirstName = "A", LastName = "B", Email = "taken@test.com", Password = "Pass@1234"
        });

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task Register_Returns400_WhenPasswordWeak()
    {
        _userSvc.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
        _authSvc.Setup(a => a.VerifyPassword("weakpass"))
                .ReturnsAsync(new UserResponseDTO { Success = false, Message = "Password too weak." });

        var result = await _sut.Register(new UserRegisterRequestDTO
        {
            FirstName = "A", LastName = "B", Email = "new@test.com", Password = "weakpass"
        });

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Register_Returns200_WhenRegistrationSucceeds()
    {
        var newUser = TestDataFactory.CreateUser(email: "new@test.com");
        newUser.EmailConfirmationToken = "email-token";

        // Primul apel verifică dacă există deja un utilizator (null = emailul e liber),
        // al doilea apel preia utilizatorul tocmai creat pentru a-i trimite emailul de confirmare.
        _userSvc.SetupSequence(s => s.GetUserByEmailAsync("new@test.com"))
                .ReturnsAsync((User?)null)
                .ReturnsAsync(newUser);
        _authSvc.Setup(a => a.VerifyPassword("Valid@1234"))
                .ReturnsAsync(new UserResponseDTO { Success = true });
        _userSvc.Setup(s => s.CreateUserAsync(It.IsAny<UserRegisterRequestDTO>()))
                .ReturnsAsync(true);
        _emailSvc.Setup(e => e.SendRegistrationSuccessEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                 .Returns(Task.CompletedTask);

        var result = await _sut.Register(new UserRegisterRequestDTO
        {
            FirstName = "A", LastName = "B", Email = "new@test.com", Password = "Valid@1234",
            DataProcessingConsent = true
        });

        result.Should().BeOfType<OkObjectResult>();
    }

    // ── ForgotPassword ───────────────────────────────────────────────────────

    [Fact]
    public async Task ForgotPassword_Returns400_WhenEmailMissing()
    {
        var result = await _sut.ForgotPassword(new UserForgotPasswordRequestDTO { Email = "" });

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ForgotPassword_Returns200_WhenUserNotFound()
    {
        // Returnează 200 generic indiferent de rezultat, ca să nu se poată deduce dacă un email există în sistem
        // (altfel un atacator ar putea enumera adresele înregistrate testând endpoint-ul în bulk)
        _userSvc.Setup(s => s.GetUserByEmailAsync("ghost@test.com")).ReturnsAsync((User?)null);

        var result = await _sut.ForgotPassword(new UserForgotPasswordRequestDTO { Email = "ghost@test.com" });

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task ForgotPassword_Returns200_WhenGoogleUser()
    {
        // Tot 200 generic — un cont Google nu are parolă locală de resetat, dar nu trebuie să dezvăluim asta apelantului
        var user = TestDataFactory.CreateUser(email: "google@test.com");
        user.Provider = "Google";
        _userSvc.Setup(s => s.GetUserByEmailAsync("google@test.com")).ReturnsAsync(user);

        var result = await _sut.ForgotPassword(new UserForgotPasswordRequestDTO { Email = "google@test.com" });

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task ForgotPassword_Returns400_WhenEmailNotConfirmed()
    {
        var user = TestDataFactory.CreateUser(email: "unconfirmed@test.com", emailConfirmed: false);
        user.Provider = "Local";
        _userSvc.Setup(s => s.GetUserByEmailAsync("unconfirmed@test.com")).ReturnsAsync(user);

        var result = await _sut.ForgotPassword(new UserForgotPasswordRequestDTO { Email = "unconfirmed@test.com" });

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ForgotPassword_Returns200_WhenValid()
    {
        var user = TestDataFactory.CreateUser(email: "valid@test.com");
        user.Provider           = "Local";
        user.PasswordResetToken = "reset-token";

        _userSvc.Setup(s => s.GetUserByEmailAsync("valid@test.com")).ReturnsAsync(user);
        _userSvc.Setup(s => s.InitiatePasswordResetAsync(user)).Returns(Task.CompletedTask);
        _emailSvc.Setup(e => e.SendPasswordResetEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                 .Returns(Task.CompletedTask);

        var result = await _sut.ForgotPassword(new UserForgotPasswordRequestDTO { Email = "valid@test.com" });

        result.Should().BeOfType<OkObjectResult>();
    }

    // ── ResetPassword ────────────────────────────────────────────────────────

    [Fact]
    public async Task ResetPassword_Returns400_WhenTokenMissing()
    {
        var result = await _sut.ResetPassword(new UserResetPasswordRequestDTO
        {
            Token = "", NewPassword = "New@1234", ConfirmPassword = "New@1234"
        });

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ResetPassword_Returns400_WhenTokenExpired()
    {
        _userSvc.Setup(s => s.GetUserByResetTokenAsync("expired-token")).ReturnsAsync((User?)null);

        var result = await _sut.ResetPassword(new UserResetPasswordRequestDTO
        {
            Token = "expired-token", NewPassword = "New@1234", ConfirmPassword = "New@1234"
        });

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ResetPassword_Returns400_WhenPasswordsMismatch()
    {
        var user = TestDataFactory.CreateUser();
        user.PasswordResetToken   = "valid-token";
        user.PasswordResetExpires = DateTime.UtcNow.AddHours(1);
        _userSvc.Setup(s => s.GetUserByResetTokenAsync("valid-token")).ReturnsAsync(user);

        var result = await _sut.ResetPassword(new UserResetPasswordRequestDTO
        {
            Token = "valid-token", NewPassword = "New@1234", ConfirmPassword = "Different@1234"
        });

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ResetPassword_Returns200_WhenValid()
    {
        var user = TestDataFactory.CreateUser();
        user.PasswordResetToken   = "valid-token";
        user.PasswordResetExpires = DateTime.UtcNow.AddHours(1);

        _userSvc.Setup(s => s.GetUserByResetTokenAsync("valid-token")).ReturnsAsync(user);
        _userSvc.Setup(s => s.PasswordChangeAsync(user, "New@1234")).Returns(Task.CompletedTask);

        var result = await _sut.ResetPassword(new UserResetPasswordRequestDTO
        {
            Token = "valid-token", NewPassword = "New@1234", ConfirmPassword = "New@1234"
        });

        result.Should().BeOfType<OkObjectResult>();
    }
}
