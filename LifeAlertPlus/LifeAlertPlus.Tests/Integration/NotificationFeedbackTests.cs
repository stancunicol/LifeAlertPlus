using System.Security.Claims;
using FluentAssertions;
using LifeAlertPlus.API.Controllers;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Infrastructure.Context;
using LifeAlertPlus.Shared.DTOs.Requests.Notification;
using LifeAlertPlus.Shared.DTOs.Responses.Notification;
using LifeAlertPlus.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LifeAlertPlus.Tests.Integration;

public class NotificationFeedbackTests : IDisposable
{
    private readonly LifeAlertPlusDbContext _ctx;
    private readonly NotificationController _sut;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _monitoredId = Guid.NewGuid();

    public NotificationFeedbackTests()
    {
        _ctx = TestDataFactory.CreateInMemoryDbContext();

        _ctx.Roles.Add(TestDataFactory.CreateUserRole());
        var user = TestDataFactory.CreateUser(_userId);
        _ctx.Users.Add(user);
        var monitored = TestDataFactory.CreateMonitored(_monitoredId);
        _ctx.Monitoreds.Add(monitored);
        _ctx.SaveChanges();

        _sut = new NotificationController(_ctx)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, _userId.ToString()) }, "Test"))
                }
            }
        };
    }

    public void Dispose() => _ctx.Dispose();

    private Notification AddNotification(
        bool feedbackRequested = false,
        bool? wasReal = null,
        DateTime? createdAt = null,
        Guid? userOverride = null,
        bool deleted = false)
    {
        var n = new Notification
        {
            Id = Guid.NewGuid(),
            IdUser = userOverride ?? _userId,
            IdMonitored = _monitoredId,
            NotificationType = "Alert",
            Message = "Test alert message",
            CreatedAt = createdAt ?? DateTime.UtcNow.AddMinutes(-30),
            FeedbackRequestedAt = feedbackRequested ? DateTime.UtcNow.AddMinutes(-5) : (DateTime?)null,
            WasReal = wasReal,
            DeletedAt = deleted ? DateTime.UtcNow : (DateTime?)null
        };
        _ctx.Notifications.Add(n);
        _ctx.SaveChanges();
        return n;
    }

    // ── GetPendingFeedback ───────────────────────────────────────────────────

    [Fact]
    public async Task GetPendingFeedback_ReturnsEmpty_WhenNoneMarked()
    {
        AddNotification(feedbackRequested: false);

        var result = await _sut.GetPendingFeedback();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var list = ok.Value as List<PendingFeedbackDTO>;
        list.Should().NotBeNull();
        list!.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPendingFeedback_IncludesOnlyFeedbackRequested()
    {
        AddNotification(feedbackRequested: false);
        var pending = AddNotification(feedbackRequested: true);
        AddNotification(feedbackRequested: true, wasReal: true);  // already answered

        var result = await _sut.GetPendingFeedback();
        var list = ((OkObjectResult)result).Value as List<PendingFeedbackDTO>;

        list.Should().NotBeNull();
        list!.Should().ContainSingle().Which.Id.Should().Be(pending.Id);
    }

    [Fact]
    public async Task GetPendingFeedback_ExcludesSoftDeletedNotifications()
    {
        AddNotification(feedbackRequested: true, deleted: true);

        var result = await _sut.GetPendingFeedback();
        var list = ((OkObjectResult)result).Value as List<PendingFeedbackDTO>;

        list!.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPendingFeedback_ScopesToCallingUser()
    {
        var otherUserId = Guid.NewGuid();
        _ctx.Users.Add(TestDataFactory.CreateUser(otherUserId, email: "other@example.com"));
        _ctx.SaveChanges();

        AddNotification(feedbackRequested: true, userOverride: otherUserId);
        var mine = AddNotification(feedbackRequested: true);

        var result = await _sut.GetPendingFeedback();
        var list = ((OkObjectResult)result).Value as List<PendingFeedbackDTO>;

        list.Should().ContainSingle().Which.Id.Should().Be(mine.Id);
    }

    [Fact]
    public async Task GetPendingFeedback_PopulatesMonitoredName()
    {
        AddNotification(feedbackRequested: true);

        var result = await _sut.GetPendingFeedback();
        var list = ((OkObjectResult)result).Value as List<PendingFeedbackDTO>;

        list!.Single().MonitoredName.Should().Be("Ion Popescu");
    }

    // ── SubmitFeedback ───────────────────────────────────────────────────────

    [Fact]
    public async Task SubmitFeedback_Returns404_WhenNotificationNotMarkedPending()
    {
        var n = AddNotification(feedbackRequested: false);

        var result = await _sut.SubmitFeedback(n.Id, new NotificationFeedbackRequestDTO { WasReal = true });

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task SubmitFeedback_Returns404_WhenBelongsToOtherUser()
    {
        var otherUserId = Guid.NewGuid();
        _ctx.Users.Add(TestDataFactory.CreateUser(otherUserId, email: "other@example.com"));
        _ctx.SaveChanges();
        var n = AddNotification(feedbackRequested: true, userOverride: otherUserId);

        var result = await _sut.SubmitFeedback(n.Id, new NotificationFeedbackRequestDTO { WasReal = true });

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task SubmitFeedback_PersistsWasReal_AndTimestamp()
    {
        var n = AddNotification(feedbackRequested: true);

        var result = await _sut.SubmitFeedback(n.Id, new NotificationFeedbackRequestDTO { WasReal = true });

        result.Should().BeOfType<NoContentResult>();
        var reloaded = _ctx.Notifications.Single(x => x.Id == n.Id);
        reloaded.WasReal.Should().BeTrue();
        reloaded.FeedbackRespondedAt.Should().NotBeNull();
        reloaded.FeedbackRespondedAt!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SubmitFeedback_AcceptsFalseAnswer()
    {
        var n = AddNotification(feedbackRequested: true);

        await _sut.SubmitFeedback(n.Id, new NotificationFeedbackRequestDTO { WasReal = false });

        _ctx.Notifications.Single(x => x.Id == n.Id).WasReal.Should().BeFalse();
    }

    [Fact]
    public async Task SubmitFeedback_RemovesFromPendingList_AfterAnswer()
    {
        var n = AddNotification(feedbackRequested: true);
        await _sut.SubmitFeedback(n.Id, new NotificationFeedbackRequestDTO { WasReal = true });

        var list = ((OkObjectResult)await _sut.GetPendingFeedback()).Value as List<PendingFeedbackDTO>;
        list!.Should().BeEmpty();
    }
}
