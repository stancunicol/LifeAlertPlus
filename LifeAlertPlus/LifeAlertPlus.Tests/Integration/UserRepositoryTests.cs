using FluentAssertions;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Infrastructure.Repositories;
using LifeAlertPlus.Tests.Helpers;

namespace LifeAlertPlus.Tests.Integration;

// Each test class gets its own in-memory database to keep tests isolated.
public class UserRepositoryTests : IDisposable
{
    private readonly LifeAlertPlus.Infrastructure.Context.LifeAlertPlusDbContext _ctx;
    private readonly UserRepository _sut;

    public UserRepositoryTests()
    {
        _ctx = TestDataFactory.CreateInMemoryDbContext();
        _sut = new UserRepository(_ctx);
        SeedRole();
    }

    private void SeedRole()
    {
        _ctx.Roles.Add(TestDataFactory.CreateUserRole());
        _ctx.SaveChanges();
    }

    public void Dispose() => _ctx.Dispose();

    // ── CreateUserAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateUserAsync_PersistsUser()
    {
        var user = TestDataFactory.CreateUser();
        var result = await _sut.CreateUserAsync(user);

        result.Should().BeTrue();
        _ctx.Users.Should().ContainSingle(u => u.Id == user.Id);
    }

    // ── GetUserByEmailAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetUserByEmailAsync_ReturnsUser_WhenExists()
    {
        var user = TestDataFactory.CreateUser(email: "find@test.com");
        await _sut.CreateUserAsync(user);

        var found = await _sut.GetUserByEmailAsync("find@test.com");

        found.Should().NotBeNull();
        found!.Email.Should().Be("find@test.com");
    }

    [Fact]
    public async Task GetUserByEmailAsync_ReturnsNull_WhenNotExists()
    {
        var found = await _sut.GetUserByEmailAsync("nobody@test.com");
        found.Should().BeNull();
    }

    // ── GetUserByIdAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetUserByIdAsync_ReturnsUser_WhenExists()
    {
        var user = TestDataFactory.CreateUser();
        await _sut.CreateUserAsync(user);

        var found = await _sut.GetUserByIdAsync(user.Id);
        found.Should().NotBeNull();
        found!.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task GetUserByIdAsync_ReturnsNull_WhenNotExists()
    {
        var found = await _sut.GetUserByIdAsync(Guid.NewGuid());
        found.Should().BeNull();
    }

    // ── UpdateUserAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateUserAsync_PersistsChanges()
    {
        var user = TestDataFactory.CreateUser();
        await _sut.CreateUserAsync(user);

        user.FirstName = "Updated";
        await _sut.UpdateUserAsync(user);

        var updated = await _sut.GetUserByIdAsync(user.Id);
        updated!.FirstName.Should().Be("Updated");
    }

    // ── GetAllUsersAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllUsersAsync_ReturnsAllUsers()
    {
        await _sut.CreateUserAsync(TestDataFactory.CreateUser(email: "u1@test.com"));
        await _sut.CreateUserAsync(TestDataFactory.CreateUser(email: "u2@test.com"));

        var all = await _sut.GetAllUsersAsync();
        all.Should().HaveCount(2);
    }

    // ── GetUserByEmailConfirmationTokenAsync ─────────────────────────────────

    [Fact]
    public async Task GetUserByEmailConfirmationToken_FindsUser()
    {
        var user = TestDataFactory.CreateUser();
        user.EmailConfirmationToken   = "confirm-abc";
        user.EmailConfirmationExpires = DateTime.UtcNow.AddHours(1);
        await _sut.CreateUserAsync(user);

        var found = await _sut.GetUserByEmailConfirmationTokenAsync("confirm-abc");
        found.Should().NotBeNull();
        found!.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task GetUserByEmailConfirmationToken_ReturnsNull_ForUnknownToken()
    {
        var found = await _sut.GetUserByEmailConfirmationTokenAsync("nonexistent-token");
        found.Should().BeNull();
    }

    // ── GetUserByPasswordResetTokenAsync ────────────────────────────────────

    [Fact]
    public async Task GetUserByPasswordResetToken_FindsUser()
    {
        var user = TestDataFactory.CreateUser();
        user.PasswordResetToken   = "reset-xyz";
        user.PasswordResetExpires = DateTime.UtcNow.AddHours(1);
        await _sut.CreateUserAsync(user);

        var found = await _sut.GetUserByPasswordResetTokenAsync("reset-xyz");
        found.Should().NotBeNull();
        found!.Id.Should().Be(user.Id);
    }

    // ── GetUserByEmailChangeTokenAsync ───────────────────────────────────────

    [Fact]
    public async Task GetUserByEmailChangeToken_FindsUser()
    {
        var user = TestDataFactory.CreateUser();
        user.EmailChangeToken   = "change-tok";
        user.EmailChangeExpires = DateTime.UtcNow.AddHours(1);
        await _sut.CreateUserAsync(user);

        var found = await _sut.GetUserByEmailChangeTokenAsync("change-tok");
        found.Should().NotBeNull();
    }

    // ── GetUserByEmailChangeCancelTokenAsync ─────────────────────────────────

    [Fact]
    public async Task GetUserByEmailChangeCancelToken_FindsUser()
    {
        var user = TestDataFactory.CreateUser();
        user.EmailChangeCancelToken = "cancel-tok";
        user.EmailChangeExpires     = DateTime.UtcNow.AddHours(1);
        await _sut.CreateUserAsync(user);

        var found = await _sut.GetUserByEmailChangeCancelTokenAsync("cancel-tok");
        found.Should().NotBeNull();
    }

    // ── DeleteUserAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteUserAsync_RemovesUser()
    {
        var user = TestDataFactory.CreateUser();
        await _sut.CreateUserAsync(user);

        var deleted = await _sut.DeleteUserAsync(user.Id);

        deleted.Should().BeTrue();
        _ctx.Users.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteUserAsync_ReturnsFalse_WhenUserNotFound()
    {
        var result = await _sut.DeleteUserAsync(Guid.NewGuid());
        result.Should().BeFalse();
    }
}
