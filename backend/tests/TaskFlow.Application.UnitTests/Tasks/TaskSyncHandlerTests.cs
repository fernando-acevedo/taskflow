using Microsoft.EntityFrameworkCore;
using NSubstitute;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Activity;
using TaskFlow.Application.Projects;
using TaskFlow.Application.Tasks;
using TaskFlow.Domain.Activity;
using TaskFlow.Domain.ApiKeys;
using TaskFlow.Domain.Clients;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.UnitTests.Tasks;

public sealed class TaskSyncHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid OutsiderId = Guid.NewGuid();
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly IAppDbContext _db = Substitute.For<IAppDbContext>();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly ITransactionRunner _transactions = Substitute.For<ITransactionRunner>();
    private readonly List<TaskItem> _added = [];
    private readonly DbSet<TaskItem> _taskSet;
    private readonly DbSet<ActivityEntry> _activitySet = FakeDbSet.Of<ActivityEntry>();
    private readonly List<Label> _addedLabels = [];
    private readonly DbSet<Label> _labelSet = FakeDbSet.Of<Label>();
    private readonly Project _project;
    private readonly TaskItem _existing;

    public TaskSyncHandlerTests()
    {
        _project = Project.Create(Client.Create("Acme", null, null, Now), "Website", null, OwnerId, Now).Value;
        _existing = TaskItem.Create(_project, "Old title", null, TaskPriority.Low, null, null, OwnerId, 0, Now).Value;
        _existing.SetPlanning(_project, "T-1", null, false, null, Now);
        _existing.TakeDomainEvents(); // as if loaded from the database
        _taskSet = FakeDbSet.Of(_existing);
        _taskSet.When(set => set.Add(Arg.Any<TaskItem>())).Do(call => _added.Add(call.Arg<TaskItem>()));
        _labelSet.When(set => set.Add(Arg.Any<Label>())).Do(call => _addedLabels.Add(call.Arg<Label>()));
        _db.Labels.Returns(_labelSet);
        var projects = FakeDbSet.Of(_project);
        var dependencies = FakeDbSet.Of<TaskDependency>();
        _db.Projects.Returns(projects);
        _db.Tasks.Returns(_taskSet);
        _db.TaskDependencies.Returns(dependencies);
        _db.ActivityEntries.Returns(_activitySet);
        _user.Id.Returns(OwnerId);
        _transactions
            .RunSerializableAsync(Arg.Any<Func<CancellationToken, Task<Result>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<Result>>>()(call.Arg<CancellationToken>()));
    }

    private SyncTasksHandler Handler()
    {
        var upserter = new TaskUpserter(
            _db, _user, new ActivityLog(_db, _user, new FixedTime(Now)), Substitute.For<IUniqueViolationDetector>(), new FixedTime(Now));
        return new SyncTasksHandler(new ProjectAccess(_db, _user), new TaskUpsertFieldsValidator(), upserter, _transactions);
    }

    private static SyncTaskItem Item(string key, string title = "Title", Extras? extras = null) =>
        new(key, new TaskUpsertFields(
            title,
            null,
            TaskPriority.High,
            null,
            extras?.Start,
            extras?.Due,
            null,
            extras?.Status,
            extras?.Blocked ?? false,
            extras?.Note,
            extras?.DependsOn,
            extras?.Labels));

    private Task<Result<SyncTasksResult>> Sync(params SyncTaskItem[] items) =>
        Handler().HandleAsync(new SyncTasksCommand(_project.Id, items), Ct);

    private sealed record Extras(
        DateOnly? Start = null,
        DateOnly? Due = null,
        TaskItemStatus? Status = null,
        bool Blocked = false,
        string? Note = null,
        string[]? DependsOn = null,
        string[]? Labels = null);

    [Fact]
    public async Task CreatesNewKeysAndUpdatesExistingOnes()
    {
        var result = await Sync(Item("T-1", "New title"), Item("T-2"));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Applied.ShouldBeTrue();
        result.Value.Items.Select(i => i.Outcome).ShouldBe([SyncOutcome.Updated, SyncOutcome.Created]);
        result.Value.Items[0].TaskId.ShouldBe(_existing.Id);
        _existing.Title.ShouldBe("New title");
        _existing.Priority.ShouldBe(TaskPriority.High);
        var created = _added.ShouldHaveSingleItem();
        created.ExternalKey.ShouldBe("T-2");
        created.CreatedById.ShouldBe(OwnerId);
        await _db.Received().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StatusChangesUseTheKanbanMove()
    {
        var result = await Sync(Item("T-1", "Old title", new Extras(Status: TaskItemStatus.Done)));

        result.Value.Applied.ShouldBeTrue();
        _existing.Status.ShouldBe(TaskItemStatus.Done);
        _existing.CompletedAt.ShouldBe(Now);
        await EventPipeline.BeforeCommitAsync(_db, _user, _existing);
        _activitySet.Received().Add(Arg.Is<ActivityEntry>(entry => entry.Type == ActivityType.TaskMoved && entry.TaskItemId == _existing.Id));
    }

    [Fact]
    public async Task StatusIsKeptWhenOmitted()
    {
        await Sync(Item("T-1"));

        _existing.Status.ShouldBe(TaskItemStatus.ToDo);
    }

    [Fact]
    public async Task SetsThePlanningFields()
    {
        await Sync(Item("T-1", "Old title", new Extras(Start: new DateOnly(2026, 11, 9), Due: new DateOnly(2026, 11, 20), Blocked: true, Note: "waiting")));

        _existing.StartDate.ShouldBe(new DateOnly(2026, 11, 9));
        _existing.DueDate.ShouldBe(new DateOnly(2026, 11, 20));
        _existing.IsBlocked.ShouldBeTrue();
        _existing.StatusNote.ShouldBe("waiting");
    }

    [Fact]
    public async Task MovingThePlanToEarlierDatesWorks()
    {
        await Sync(Item("T-1", "Old title", new Extras(Start: new DateOnly(2026, 12, 1), Due: new DateOnly(2026, 12, 5))));

        var result = await Sync(Item("T-1", "Old title", new Extras(Start: new DateOnly(2026, 11, 1), Due: new DateOnly(2026, 11, 5))));

        result.Value.Applied.ShouldBeTrue();
        _existing.DueDate.ShouldBe(new DateOnly(2026, 11, 5));
    }

    [Fact]
    public async Task DependenciesResolveKeysOfTheSameRequestInAnyOrder()
    {
        var result = await Sync(
            Item("T-2", "Second", new Extras(DependsOn: ["T-3", "T-1"])),
            Item("T-3", "Third"));

        result.Value.Applied.ShouldBeTrue();
        var second = _added.Single(task => task.ExternalKey == "T-2");
        var third = _added.Single(task => task.ExternalKey == "T-3");
        second.Dependencies.Select(d => d.DependsOnTaskId).ShouldBe([third.Id, _existing.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task ACycleFailsTheItemAndTheWholeBatch()
    {
        var result = await Sync(
            Item("T-1", "Old title", new Extras(DependsOn: ["T-2"])),
            Item("T-2", "Second", new Extras(DependsOn: ["T-1"])));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Applied.ShouldBeFalse();
        result.Value.Items[0].Outcome.ShouldBe(SyncOutcome.NotApplied);
        result.Value.Items[1].Outcome.ShouldBe(SyncOutcome.Failed);
        result.Value.Items[1].ErrorCode.ShouldBe(TaskErrors.DependencyCycle.Code);
    }

    [Fact]
    public async Task OneFailureSavesNothingMore()
    {
        var result = await Sync(Item("T-2"), Item("T-3", " "), Item("T-4"));

        result.Value.Applied.ShouldBeFalse();
        result.Value.Items.Select(i => i.Outcome).ShouldBe([SyncOutcome.NotApplied, SyncOutcome.Failed, SyncOutcome.NotApplied]);
        result.Value.Items[1].ErrorCode.ShouldBe("Validation");
        // Only the first item is saved, inside the transaction that the runner rolls back.
        await _db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnUnknownDependencyKeyFailsTheItem()
    {
        var result = await Sync(Item("T-2", "Second", new Extras(DependsOn: ["NOPE"])));

        result.Value.Applied.ShouldBeFalse();
        result.Value.Items.ShouldHaveSingleItem().ErrorCode.ShouldBe(TaskSyncErrors.DependencyKeyNotFound.Code);
    }

    [Fact]
    public async Task ARepeatedKeyInTheRequestFails()
    {
        var result = await Sync(Item("T-2"), Item("t-2"));

        result.Value.Items[1].ErrorCode.ShouldBe(TaskSyncErrors.DuplicateExternalKey.Code);
    }

    [Fact]
    public async Task StartAfterDueFailsTheItem()
    {
        var result = await Sync(Item("T-1", "Old title", new Extras(Start: new DateOnly(2026, 12, 1), Due: new DateOnly(2026, 11, 1))));

        result.Value.Items.ShouldHaveSingleItem().ErrorCode.ShouldBe(TaskErrors.StartAfterDue.Code);
    }

    [Fact]
    public async Task RejectsEmptyAndOversizedBatches()
    {
        (await Sync()).Error.ShouldBe(TaskSyncErrors.SyncEmpty);

        var many = Enumerable.Range(0, SyncTasksHandler.MaxItems + 1).Select(i => Item("K-" + i)).ToArray();
        (await Sync(many)).Error.ShouldBe(TaskSyncErrors.SyncTooLarge);
    }

    [Fact]
    public async Task RequiresWorkAccessToTheProject()
    {
        _user.Id.Returns(OutsiderId);

        (await Sync(Item("T-2"))).Error.ShouldBe(ProjectErrors.Forbidden);
    }

    [Fact]
    public async Task AReadOnlyApiKeyCannotSync()
    {
        _user.ApiKeyId.Returns(Guid.NewGuid());
        _user.ApiKeyScope.Returns(ApiKeyScope.Read);

        (await Sync(Item("T-2"))).Error.ShouldBe(ApiKeyErrors.ScopeInsufficient);
    }

    [Fact]
    public async Task AWriteApiKeyCanSyncAndLeavesItsIdInTheActivity()
    {
        var keyId = Guid.NewGuid();
        _user.ApiKeyId.Returns(keyId);
        _user.ApiKeyScope.Returns(ApiKeyScope.Write);

        var result = await Sync(Item("T-2"));

        result.Value.Applied.ShouldBeTrue();
        await EventPipeline.BeforeCommitAsync(_db, _user, _added.ToArray());
        _activitySet.Received().Add(Arg.Is<ActivityEntry>(entry => entry.ApiKeyId == keyId));
    }

    [Fact]
    public async Task ArchivedProjectFailsEveryItem()
    {
        _project.Archive(Now);

        var result = await Sync(Item("T-1"), Item("T-2"));

        result.Value.Applied.ShouldBeFalse();
        result.Value.Items.ShouldAllBe(i => i.Outcome == SyncOutcome.Failed && i.ErrorCode == ProjectErrors.Archived.Code);
    }

    [Fact]
    public async Task LabelsReplaceTheTaskLabelsAndMissingOnesAreCreatedByAnOwner()
    {
        var result = await Sync(Item("T-1", "Old title", new Extras(Labels: ["agent:backend", "AGENT:BACKEND", "Urgent"])));

        result.Value.Applied.ShouldBeTrue();
        _addedLabels.Select(label => label.Name).ShouldBe(["agent:backend", "Urgent"]);
        _existing.Labels.Select(label => label.Name).ShouldBe(["agent:backend", "Urgent"]);
        _activitySet.Received(2).Add(Arg.Is<ActivityEntry>(entry => entry.Type == ActivityType.LabelCreated));
    }

    [Fact]
    public async Task LabelsAreKeptWhenOmittedAndClearedWhenEmpty()
    {
        var label = Label.Create(_project, "agent:gantt", "#112233", [], Now).Value;
        var labelsWithOne = FakeDbSet.Of(label);
        _db.Labels.Returns(labelsWithOne);
        await Sync(Item("T-1", "Old title", new Extras(Labels: ["agent:gantt"])));
        _existing.Labels.ShouldHaveSingleItem().ShouldBe(label);

        await Sync(Item("T-1"));
        _existing.Labels.Count.ShouldBe(1);

        await Sync(Item("T-1", "Old title", new Extras(Labels: [])));
        _existing.Labels.ShouldBeEmpty();
        _addedLabels.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnApiKeyCannotCreateALabelSoTheItemFails()
    {
        _user.ApiKeyId.Returns(Guid.NewGuid());
        _user.ApiKeyScope.Returns(ApiKeyScope.Write);

        var result = await Sync(Item("T-1", "Old title", new Extras(Labels: ["agent:new"])));

        result.Value.Applied.ShouldBeFalse();
        result.Value.Items.ShouldHaveSingleItem().ErrorCode.ShouldBe("Labels.CreateNotAllowed");
        _addedLabels.ShouldBeEmpty();
    }

    [Fact]
    public async Task ABlankOrTooLongLabelNameFailsValidation()
    {
        (await Sync(Item("T-1", "Old title", new Extras(Labels: [" "])))).Value.Items.ShouldHaveSingleItem().ErrorCode.ShouldBe("Validation");
        (await Sync(Item("T-1", "Old title", new Extras(Labels: [new string('x', 41)])))).Value.Items.ShouldHaveSingleItem().ErrorCode.ShouldBe("Validation");
    }

    [Fact]
    public async Task UpsertUpdatesTheTaskWithThatKeyAndReportsItWasNotCreated()
    {
        var upserter = new TaskUpserter(
            _db, _user, new ActivityLog(_db, _user, new FixedTime(Now)), Substitute.For<IUniqueViolationDetector>(), new FixedTime(Now));
        var access = new ProjectAccess(_db, _user);
        var handler = new UpsertTaskHandler(
            access, new TaskUpsertFieldsValidator(), upserter, new GetTaskHandler(_db, access, new FixedTime(Now)), _transactions);
        var fields = new TaskUpsertFields("Renamed", null, TaskPriority.Critical, IsBlocked: true, StatusNote: "stuck");

        var result = await handler.HandleAsync(new UpsertTaskCommand(_project.Id, "T-1", fields), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Created.ShouldBeFalse();
        result.Value.Task.Id.ShouldBe(_existing.Id);
        result.Value.Task.Title.ShouldBe("Renamed");
        result.Value.Task.IsBlocked.ShouldBeTrue();
        result.Value.Task.ExternalKey.ShouldBe("T-1");
    }

    [Fact]
    public async Task UpsertRejectsInvalidFields()
    {
        var upserter = new TaskUpserter(
            _db, _user, new ActivityLog(_db, _user, new FixedTime(Now)), Substitute.For<IUniqueViolationDetector>(), new FixedTime(Now));
        var access = new ProjectAccess(_db, _user);
        var handler = new UpsertTaskHandler(
            access, new TaskUpsertFieldsValidator(), upserter, new GetTaskHandler(_db, access, new FixedTime(Now)), _transactions);

        var result = await handler.HandleAsync(
            new UpsertTaskCommand(_project.Id, "T-1", new TaskUpsertFields("x", null, StatusNote: new string('n', 501))), Ct);

        result.Error!.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public async Task AReadKeyGets403BeforeTheBodyIsChecked()
    {
        _user.ApiKeyId.Returns(Guid.NewGuid());
        _user.ApiKeyScope.Returns(TaskFlow.Domain.ApiKeys.ApiKeyScope.Read);

        (await Handler().HandleAsync(new SyncTasksCommand(_project.Id, []), Ct)).Error.ShouldBe(TaskFlow.Domain.ApiKeys.ApiKeyErrors.ScopeInsufficient);

        var upserter = new TaskUpserter(
            _db, _user, new ActivityLog(_db, _user, new FixedTime(Now)), Substitute.For<IUniqueViolationDetector>(), new FixedTime(Now));
        var access = new ProjectAccess(_db, _user);
        var handler = new UpsertTaskHandler(
            access, new TaskUpsertFieldsValidator(), upserter, new GetTaskHandler(_db, access, new FixedTime(Now)), _transactions);
        var invalid = new TaskUpsertFields("x", null, StatusNote: new string('n', 501));
        (await handler.HandleAsync(new UpsertTaskCommand(_project.Id, "T-1", invalid), Ct)).Error.ShouldBe(TaskFlow.Domain.ApiKeys.ApiKeyErrors.ScopeInsufficient);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
