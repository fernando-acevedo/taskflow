using Microsoft.EntityFrameworkCore;
using NSubstitute;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Notifications;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Notifications;

namespace TaskFlow.Application.UnitTests.Notifications;

public sealed class NotifierTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly Guid OtherId = Guid.NewGuid();

    private readonly IAppDbContext _db = Substitute.For<IAppDbContext>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly INotificationEmailSender _emails = Substitute.For<INotificationEmailSender>();
    private readonly DbSet<Notification> _notifications = FakeDbSet.Of<Notification>();
    private readonly Guid _projectId = Guid.NewGuid();
    private readonly Guid _taskId = Guid.NewGuid();

    public NotifierTests()
    {
        _currentUser.Id.Returns(ActorId);
        _db.Notifications.Returns(_notifications);
        _identity.FindByIdAsync(ActorId, Arg.Any<CancellationToken>())
            .Returns(new UserDto(ActorId, "actor@tests.local", "Actor", true, [], Now));
    }

    [Fact]
    public async Task NeverNotifiesTheActor()
    {
        await Notifier().NotifyAsync(
            NotificationType.TaskAssigned, ActorId, _projectId, "Website", _taskId, "Design", null, TestContext.Current.CancellationToken);

        _notifications.DidNotReceive().Add(Arg.Any<Notification>());
    }

    [Fact]
    public async Task StoresTheValuesTheTextNeeds()
    {
        await Notifier().NotifyAsync(
            NotificationType.Mentioned, OtherId, _projectId, "Website", _taskId, "Design", Guid.NewGuid(), TestContext.Current.CancellationToken);

        _notifications.Received(1).Add(Arg.Is<Notification>(notification =>
            notification.UserId == OtherId
            && notification.ActorId == ActorId
            && notification.TaskItemId == _taskId
            && notification.Data.Contains("\"actorName\":\"Actor\"")
            && notification.Data.Contains("\"taskTitle\":\"Design\"")
            && notification.Data.Contains("commentId")));
    }

    [Fact]
    public async Task EmailsOnlyUsersWhoAskedForThatKind()
    {
        var preferences = NotificationPreferences.CreateDefault(OtherId);
        preferences.Update(emailOnAssigned: true, emailOnMentioned: false, emailOnCommentOnMyTask: false, emailDailyDigest: false);
        var preferenceSet = FakeDbSet.Of(preferences);
        _db.NotificationPreferences.Returns(preferenceSet);
        _identity.FindByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new UserDto(OtherId, "other@tests.local", "Other", true, [], Now)]);
        var notifier = Notifier();

        await notifier.NotifyAsync(
            NotificationType.TaskAssigned, OtherId, _projectId, "Website", _taskId, "Design", null, TestContext.Current.CancellationToken);
        await notifier.NotifyAsync(
            NotificationType.Mentioned, OtherId, _projectId, "Website", _taskId, "Design", null, TestContext.Current.CancellationToken);
        await notifier.SendEmailsAsync(TestContext.Current.CancellationToken);

        await _emails.Received(1).SendAsync(
            Arg.Any<UserDto>(),
            Arg.Is<IReadOnlyList<Notification>>(list => list.Count == 1 && list[0].Type == NotificationType.TaskAssigned),
            Arg.Any<CancellationToken>());
    }

    private Notifier Notifier() => new(_db, _currentUser, _identity, _emails, new FixedTime(Now));

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
