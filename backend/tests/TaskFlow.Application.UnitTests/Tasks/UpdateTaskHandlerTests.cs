using Microsoft.EntityFrameworkCore;
using NSubstitute;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Activity;
using TaskFlow.Application.Notifications;
using TaskFlow.Application.Projects;
using TaskFlow.Application.Tasks;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Activity;
using TaskFlow.Domain.Clients;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Notifications;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.UnitTests.Tasks;

public sealed class UpdateTaskHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly IAppDbContext _db = Substitute.For<IAppDbContext>();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly DbSet<ActivityEntry> _activitySet = FakeDbSet.Of<ActivityEntry>();
    private readonly Project _project;
    private readonly TaskItem _task;

    public UpdateTaskHandlerTests()
    {
        _project = Project.Create(Client.Create("Acme", null, null, Now), "Website", null, OwnerId, Now).Value;
        _task = TaskItem.Create(_project, "Logo", null, TaskPriority.Low, null, null, OwnerId, 0, Now).Value;
        typeof(TaskItem).GetProperty(nameof(TaskItem.RowVersion))!.SetValue(_task, new byte[] { 1 });
        var projectsSet = FakeDbSet.Of(_project);
        _db.Projects.Returns(projectsSet);
        var tasksSet = FakeDbSet.Of(_task);
        _db.Tasks.Returns(tasksSet);
        var labelsSet = FakeDbSet.Of<Label>();
        _db.Labels.Returns(labelsSet);
        _db.ActivityEntries.Returns(_activitySet);
        _user.Id.Returns(OwnerId);
    }

    private UpdateTaskHandler Handler()
    {
        var time = new FixedTime(Now);
        var access = new ProjectAccess(_db, _user);
        var notifier = new Notifier(_db, _user, Substitute.For<IIdentityService>(), Substitute.For<INotificationEmailSender>(), time);
        return new UpdateTaskHandler(
            _db,
            access,
            new UpdateTaskValidator(),
            new GetTaskHandler(_db, access, time),
            new ActivityLog(_db, _user, time),
            notifier,
            time);
    }

    private UpdateTaskCommand Command(DateOnly? start = null, DateOnly? due = null, bool blocked = false, string? note = null) =>
        new(_project.Id, _task.Id, "Logo", null, TaskPriority.Low, null, due, null, _task.RowVersion, null, start, blocked, note);

    [Fact]
    public async Task SetsTheStartDateTheBlockAndTheNote()
    {
        var result = await Handler().HandleAsync(Command(new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 20), true, "  waiting  "), Ct);

        result.IsSuccess.ShouldBeTrue();
        _task.StartDate.ShouldBe(new DateOnly(2026, 11, 9));
        _task.DueDate.ShouldBe(new DateOnly(2026, 11, 20));
        _task.IsBlocked.ShouldBeTrue();
        _task.StatusNote.ShouldBe("waiting");
        result.Value.IsBlocked.ShouldBeTrue();
        _activitySet.Received().Add(Arg.Is<ActivityEntry>(entry => entry.Type == ActivityType.TaskUpdated && entry.Changes.Contains("isBlocked")));
    }

    [Fact]
    public async Task OmittingTheFieldsClearsThem()
    {
        await Handler().HandleAsync(Command(new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 20), true, "waiting"), Ct);

        await Handler().HandleAsync(Command(), Ct);

        _task.StartDate.ShouldBeNull();
        _task.IsBlocked.ShouldBeFalse();
        _task.StatusNote.ShouldBeNull();
    }

    [Fact]
    public async Task AStartDateAfterTheDueDateIsRejectedAndNothingChanges()
    {
        var result = await Handler().HandleAsync(Command(new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 9), true, "x"), Ct);

        result.Error.ShouldBe(TaskErrors.StartAfterDue);
        _task.StartDate.ShouldBeNull();
        _task.IsBlocked.ShouldBeFalse();
    }

    [Fact]
    public async Task ANoteOver500CharactersFailsValidation()
    {
        var result = await Handler().HandleAsync(Command(note: new string('n', 501)), Ct);

        result.Error!.Type.ShouldBe(ErrorType.Validation);
        result.Error.ValidationErrors!.ShouldContainKey("statusNote");
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
