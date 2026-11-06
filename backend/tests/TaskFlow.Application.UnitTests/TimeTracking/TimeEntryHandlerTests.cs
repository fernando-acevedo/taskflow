using NSubstitute;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Projects;
using TaskFlow.Application.TimeTracking;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Clients;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.UnitTests.TimeTracking;

public sealed class TimeEntryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 19);
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();
    private static readonly Guid OtherId = Guid.NewGuid();
    private static readonly Guid OutsiderId = Guid.NewGuid();

    private readonly IAppDbContext _db = Substitute.For<IAppDbContext>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly MutableTime _time = new(Now);
    private readonly Microsoft.EntityFrameworkCore.DbSet<TimeEntry> _entrySet;
    private readonly Project _project;
    private readonly TaskItem _task;
    private readonly TimeEntry _memberEntry;

    public TimeEntryHandlerTests()
    {
        _project = Project.Create(Client.Create("Acme", null, null, Now), "Website", null, OwnerId, Now).Value;
        _project.AddMember(MemberId, ProjectRole.Member, Now);
        _project.AddMember(OtherId, ProjectRole.Member, Now);
        _task = TaskItem.Create(_project, "Design", null, TaskPriority.Medium, null, null, OwnerId, 0, Now).Value;
        _memberEntry = TimeEntry.Create(_project, _task.Id, MemberId, Today, 30, null, Today, Now).Value;
        _entrySet = FakeDbSet.Of(_memberEntry);
        var projects = FakeDbSet.Of(_project);
        var tasks = FakeDbSet.Of(_task);
        _db.Projects.Returns(projects);
        _db.Tasks.Returns(tasks);
        _db.TimeEntries.Returns(_entrySet);
        _currentUser.Id.Returns(MemberId);
        _identity.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => new UserDto(call.Arg<Guid>(), "x@tests.local", "Someone", true, [], Now));
    }

    [Fact]
    public async Task CreateLogsTimeForTheCurrentUser()
    {
        var result = await CreateHandler().HandleAsync(
            new CreateTimeEntryCommand(_project.Id, _task.Id, Today, 45, " Review "), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        (result.Value.UserId, result.Value.Minutes, result.Value.Note, result.Value.UserName).ShouldBe((MemberId, 45, "Review", "Someone"));
        _entrySet.Received(1).Add(Arg.Is<TimeEntry>(entry => entry.UserId == MemberId && entry.TaskItemId == _task.Id));
        await _db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateRejectsAFutureDateComparedWithTheClock()
    {
        var inTwoDays = await CreateHandler().HandleAsync(
            new CreateTimeEntryCommand(_project.Id, _task.Id, Today.AddDays(2), 45, null), TestContext.Current.CancellationToken);
        _time.Now = _time.Now.AddDays(1);
        var nowToday = await CreateHandler().HandleAsync(
            new CreateTimeEntryCommand(_project.Id, _task.Id, Today.AddDays(2), 45, null), TestContext.Current.CancellationToken);

        inTwoDays.Error.ShouldBe(TimeEntryErrors.FutureDate);
        nowToday.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateRejectsInvalidMinutesAsAValidationError()
    {
        var result = await CreateHandler().HandleAsync(
            new CreateTimeEntryCommand(_project.Id, _task.Id, Today, 0, null), TestContext.Current.CancellationToken);

        result.Error.ShouldNotBeNull().Code.ShouldBe("Validation");
        result.Error.ValidationErrors.ShouldNotBeNull().ShouldContainKey("minutes");
        await _db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateRejectsAnOutsiderAndAnUnknownTask()
    {
        var unknown = await CreateHandler().HandleAsync(
            new CreateTimeEntryCommand(_project.Id, Guid.NewGuid(), Today, 10, null), TestContext.Current.CancellationToken);
        _currentUser.Id.Returns(OutsiderId);
        var outsider = await CreateHandler().HandleAsync(
            new CreateTimeEntryCommand(_project.Id, _task.Id, Today, 10, null), TestContext.Current.CancellationToken);

        unknown.Error.ShouldBe(TaskErrors.NotFound);
        outsider.Error.ShouldBe(ProjectErrors.Forbidden);
    }

    [Fact]
    public async Task UpdateByTheOwnerOfTheEntryChangesIt()
    {
        var result = await UpdateHandler().HandleAsync(
            new UpdateTimeEntryCommand(_project.Id, _task.Id, _memberEntry.Id, Today, 50, "More"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        (_memberEntry.Minutes, _memberEntry.Note).ShouldBe((50, "More"));
    }

    [Fact]
    public async Task UpdateByAnotherMemberIsForbiddenButAProjectOwnerMay()
    {
        _currentUser.Id.Returns(OtherId);
        var other = await UpdateHandler().HandleAsync(
            new UpdateTimeEntryCommand(_project.Id, _task.Id, _memberEntry.Id, Today, 50, null), TestContext.Current.CancellationToken);
        _currentUser.Id.Returns(OwnerId);
        var owner = await UpdateHandler().HandleAsync(
            new UpdateTimeEntryCommand(_project.Id, _task.Id, _memberEntry.Id, Today, 50, null), TestContext.Current.CancellationToken);

        other.Error.ShouldBe(TimeEntryErrors.NotOwner);
        owner.IsSuccess.ShouldBeTrue();
        _memberEntry.UserId.ShouldBe(MemberId);
    }

    [Fact]
    public async Task UpdateAndDeleteReportAnUnknownEntry()
    {
        var update = await UpdateHandler().HandleAsync(
            new UpdateTimeEntryCommand(_project.Id, _task.Id, Guid.NewGuid(), Today, 50, null), TestContext.Current.CancellationToken);
        var delete = await DeleteHandler().HandleAsync(
            new DeleteTimeEntryCommand(_project.Id, _task.Id, Guid.NewGuid()), TestContext.Current.CancellationToken);

        update.Error.ShouldBe(TimeEntryErrors.NotFound);
        delete.Error.ShouldBe(TimeEntryErrors.NotFound);
    }

    [Fact]
    public async Task DeleteRemovesTheEntryForItsOwnerAndAProjectOwnerButNotAnotherMember()
    {
        _currentUser.Id.Returns(OtherId);
        var other = await DeleteHandler().HandleAsync(
            new DeleteTimeEntryCommand(_project.Id, _task.Id, _memberEntry.Id), TestContext.Current.CancellationToken);
        _currentUser.Id.Returns(OwnerId);
        var owner = await DeleteHandler().HandleAsync(
            new DeleteTimeEntryCommand(_project.Id, _task.Id, _memberEntry.Id), TestContext.Current.CancellationToken);

        other.Error.ShouldBe(TimeEntryErrors.NotOwner);
        owner.IsSuccess.ShouldBeTrue();
        _entrySet.Received(1).Remove(_memberEntry);
    }

    [Fact]
    public async Task AnArchivedProjectRejectsEveryChange()
    {
        _project.Archive(Now);
        _currentUser.Id.Returns(OwnerId);

        var create = await CreateHandler().HandleAsync(
            new CreateTimeEntryCommand(_project.Id, _task.Id, Today, 10, null), TestContext.Current.CancellationToken);
        var update = await UpdateHandler().HandleAsync(
            new UpdateTimeEntryCommand(_project.Id, _task.Id, _memberEntry.Id, Today, 50, null), TestContext.Current.CancellationToken);
        var delete = await DeleteHandler().HandleAsync(
            new DeleteTimeEntryCommand(_project.Id, _task.Id, _memberEntry.Id), TestContext.Current.CancellationToken);

        create.Error.ShouldBe(ProjectErrors.Archived);
        update.Error.ShouldBe(ProjectErrors.Archived);
        delete.Error.ShouldBe(ProjectErrors.Archived);
    }

    [Fact]
    public async Task SummaryRejectsAnInvalidRangeBeforeQuerying()
    {
        _currentUser.Id.Returns(OwnerId);
        var backwards = await SummaryHandler().HandleAsync(
            new GetTimeSummaryQuery(_project.Id, Today, Today.AddDays(-1), null), TestContext.Current.CancellationToken);
        var tooLong = await SummaryHandler().HandleAsync(
            new GetTimeSummaryQuery(_project.Id, Today.AddDays(-366), Today, null), TestContext.Current.CancellationToken);

        backwards.Error.ShouldBe(TimeEntryErrors.InvalidRange);
        tooLong.Error.ShouldBe(TimeEntryErrors.InvalidRange);
    }

    [Fact]
    public void CsvFieldsAreQuotedAndFormulasDefused()
    {
        ExportTimeSummaryCsvHandler.Escape(null).ShouldBe(string.Empty);
        ExportTimeSummaryCsvHandler.Escape("plain").ShouldBe("plain");
        ExportTimeSummaryCsvHandler.Escape("a,b").ShouldBe("\"a,b\"");
        ExportTimeSummaryCsvHandler.Escape("say \"hi\"").ShouldBe("\"say \"\"hi\"\"\"");
        ExportTimeSummaryCsvHandler.Escape("=1+1").ShouldBe("'=1+1");
        ExportTimeSummaryCsvHandler.Escape("  =1+1").ShouldBe("'  =1+1");
        ExportTimeSummaryCsvHandler.Escape(" \t@x").ShouldBe("' \t@x");
        ExportTimeSummaryCsvHandler.Escape("\rabc").ShouldBe("\"'\rabc\"");
        ExportTimeSummaryCsvHandler.Escape("a =b").ShouldBe("a =b");
    }

    private sealed class MutableTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private CreateTimeEntryHandler CreateHandler() =>
        new(_db, new ProjectAccess(_db, _currentUser), _currentUser, _identity, new CreateTimeEntryValidator(), _time);

    private UpdateTimeEntryHandler UpdateHandler() =>
        new(_db, new ProjectAccess(_db, _currentUser), _currentUser, _identity, new UpdateTimeEntryValidator(), _time);

    private DeleteTimeEntryHandler DeleteHandler() =>
        new(_db, new ProjectAccess(_db, _currentUser), _currentUser);

    private GetTimeSummaryHandler SummaryHandler() =>
        new(_db, new ProjectAccess(_db, _currentUser), _identity);
}
