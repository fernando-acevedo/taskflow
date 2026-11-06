using Microsoft.EntityFrameworkCore;
using NSubstitute;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Activity;
using TaskFlow.Application.Comments;
using TaskFlow.Application.Projects;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Activity;
using TaskFlow.Domain.Clients;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.UnitTests.Comments;

public sealed class TaskCommentHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();
    private static readonly Guid OutsiderId = Guid.NewGuid();

    private readonly IAppDbContext _db = Substitute.For<IAppDbContext>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly DbSet<TaskComment> _commentSet;
    private readonly DbSet<ActivityEntry> _activitySet = FakeDbSet.Of<ActivityEntry>();
    private readonly List<TaskComment> _removed = [];
    private readonly Project _project;
    private readonly TaskItem _task;

    public TaskCommentHandlerTests()
    {
        _project = Project.Create(Client.Create("Acme", null, null, Now), "Website", null, OwnerId, Now).Value;
        _project.AddMember(MemberId, ProjectRole.Member, Now);
        _task = TaskItem.Create(_project, "Design", null, TaskPriority.Medium, null, null, OwnerId, 0, Now).Value;
        var projects = FakeDbSet.Of(_project);
        var tasks = FakeDbSet.Of(_task);
        _commentSet = FakeDbSet.Of<TaskComment>();
        _db.Projects.Returns(projects);
        _db.Tasks.Returns(tasks);
        _db.TaskComments.Returns(_commentSet);
        _db.ActivityEntries.Returns(_activitySet);
        _currentUser.Id.Returns(OwnerId);
    }

    [Fact]
    public async Task CreateStoresTheCommentWithTheCurrentUserAsAuthor()
    {
        _identity.FindByIdAsync(OwnerId, Arg.Any<CancellationToken>())
            .Returns(new UserDto(OwnerId, "owner@tests.local", "Owner", true, [], Now));

        var result = await CreateHandler().HandleAsync(
            new CreateTaskCommentCommand(_project.Id, _task.Id, " Hello ", null), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Body.ShouldBe("Hello");
        result.Value.AuthorId.ShouldBe(OwnerId);
        _commentSet.Received(1).Add(Arg.Is<TaskComment>(comment => comment.AuthorId == OwnerId));
        _activitySet.Received(1).Add(Arg.Is<ActivityEntry>(entry =>
            entry.Type == ActivityType.CommentAdded && entry.TaskItemId == _task.Id && entry.ActorId == OwnerId && entry.Changes.Contains("Design")));
        await _db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateRejectsAnOutsider()
    {
        _currentUser.Id.Returns(OutsiderId);

        var result = await CreateHandler().HandleAsync(
            new CreateTaskCommentCommand(_project.Id, _task.Id, "Hello", null), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(ProjectErrors.Forbidden);
        await _db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateRejectsAnUnknownTask()
    {
        var result = await CreateHandler().HandleAsync(
            new CreateTaskCommentCommand(_project.Id, Guid.NewGuid(), "Hello", null), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(TaskErrors.NotFound);
    }

    [Fact]
    public async Task CreateRejectsAMentionOfADeactivatedUser()
    {
        _identity.FindByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new UserDto(MemberId, "member@tests.local", "Member", false, [], Now)]);

        var result = await CreateHandler().HandleAsync(
            new CreateTaskCommentCommand(_project.Id, _task.Id, "Hi", [MemberId]), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(TaskCommentErrors.MentionNotMember);
        _commentSet.DidNotReceive().Add(Arg.Any<TaskComment>());
    }

    [Fact]
    public async Task DeleteRemovesTheCommentForItsAuthor()
    {
        var comment = AddComment(OwnerId);

        var result = await DeleteHandler().HandleAsync(
            new DeleteTaskCommentCommand(_project.Id, _task.Id, comment.Id), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        _removed.ShouldBe([comment]);
    }

    [Fact]
    public async Task DeleteIsForbiddenForAnotherMemberButAllowedForAnOwner()
    {
        var comment = AddComment(MemberId);
        _project.AddMember(OutsiderId, ProjectRole.Member, Now);
        _currentUser.Id.Returns(OutsiderId);

        var byMember = await DeleteHandler().HandleAsync(
            new DeleteTaskCommentCommand(_project.Id, _task.Id, comment.Id), TestContext.Current.CancellationToken);
        _currentUser.Id.Returns(OwnerId);
        var byOwner = await DeleteHandler().HandleAsync(
            new DeleteTaskCommentCommand(_project.Id, _task.Id, comment.Id), TestContext.Current.CancellationToken);

        byMember.Error.ShouldBe(TaskCommentErrors.NotAuthorOrManager);
        byOwner.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task DeleteReturnsNotFoundForAnUnknownComment()
    {
        var result = await DeleteHandler().HandleAsync(
            new DeleteTaskCommentCommand(_project.Id, _task.Id, Guid.NewGuid()), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(TaskCommentErrors.NotFound);
    }

    [Fact]
    public async Task DeleteReturnsNotFoundWhenAnotherDeleteWonTheRace()
    {
        var comment = AddComment(OwnerId);
        _db.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns<int>(_ => throw new DbUpdateConcurrencyException());

        var result = await DeleteHandler().HandleAsync(
            new DeleteTaskCommentCommand(_project.Id, _task.Id, comment.Id), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(TaskCommentErrors.NotFound);
    }

    private TaskComment AddComment(Guid authorId)
    {
        var comment = TaskComment.Create(_project, _task.Id, authorId, "Draft", null, Now).Value;
        var set = FakeDbSet.Of(comment);
        set.When(s => s.Remove(Arg.Any<TaskComment>())).Do(call => _removed.Add(call.Arg<TaskComment>()));
        _db.TaskComments.Returns(set);
        return comment;
    }

    private CreateTaskCommentHandler CreateHandler() =>
        new(
            _db, new ProjectAccess(_db, _currentUser), _currentUser, _identity, new CreateTaskCommentValidator(), Activity(), TimeProvider.System);

    private DeleteTaskCommentHandler DeleteHandler() =>
        new(_db, new ProjectAccess(_db, _currentUser), _currentUser, Activity());

    private ActivityLog Activity() => new(_db, _currentUser, TimeProvider.System);
}
