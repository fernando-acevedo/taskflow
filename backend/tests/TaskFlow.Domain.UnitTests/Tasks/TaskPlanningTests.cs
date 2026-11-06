using TaskFlow.Domain.Clients;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Domain.UnitTests.Tasks;

public sealed class TaskPlanningTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid OwnerId = Guid.NewGuid();

    private readonly Project _project =
        Project.Create(Client.Create("Acme", null, null, Now), "Website", null, OwnerId, Now).Value;

    private TaskItem NewTask(DateOnly? due = null) =>
        TaskItem.Create(_project, "Design", null, TaskPriority.Medium, OwnerId, due, OwnerId, 0, Now).Value;

    [Fact]
    public void SetPlanningStoresTheFieldsAndTrimsText()
    {
        var task = NewTask(new DateOnly(2026, 11, 30));

        var result = task.SetPlanning(_project, " T-79 ", new DateOnly(2026, 11, 2), true, "  waiting for API  ", Now.AddHours(1));

        result.IsSuccess.ShouldBeTrue();
        task.ExternalKey.ShouldBe("T-79");
        task.StartDate.ShouldBe(new DateOnly(2026, 11, 2));
        task.IsBlocked.ShouldBeTrue();
        task.StatusNote.ShouldBe("waiting for API");
        task.UpdatedAt.ShouldBe(Now.AddHours(1));
    }

    [Fact]
    public void BlankNoteAndKeyMeanNone()
    {
        var task = NewTask();

        task.SetPlanning(_project, " ", null, false, "  ", Now).IsSuccess.ShouldBeTrue();

        task.ExternalKey.ShouldBeNull();
        task.StatusNote.ShouldBeNull();
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("a/b")]
    [InlineData("-leading")]
    [InlineData("ñandú")]
    public void ExternalKeyMustBeUrlSafe(string key)
    {
        NewTask().SetPlanning(_project, key, null, false, null, Now).Error.ShouldBe(TaskErrors.ExternalKeyInvalid);
    }

    [Fact]
    public void ExternalKeyHasALengthLimit()
    {
        var task = NewTask();

        task.SetPlanning(_project, new string('a', 101), null, false, null, Now).Error.ShouldBe(TaskErrors.ExternalKeyInvalid);
        task.SetPlanning(_project, new string('a', 100), null, false, null, Now).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void ExternalKeyCannotChangeOnceSet()
    {
        var task = NewTask();
        task.SetPlanning(_project, "A-1", null, false, null, Now);

        task.SetPlanning(_project, "a-1", null, false, null, Now).IsSuccess.ShouldBeTrue(); // same key, other case
        task.SetPlanning(_project, "B-2", null, false, null, Now).Error.ShouldBe(TaskErrors.ExternalKeyLocked);
        task.SetPlanning(_project, null, null, false, null, Now).IsSuccess.ShouldBeTrue(); // omitted: keeps it
        task.ExternalKey.ShouldBe("A-1");
    }

    [Fact]
    public void StartCannotBeAfterDue()
    {
        var task = NewTask(new DateOnly(2026, 11, 10));

        task.SetPlanning(_project, null, new DateOnly(2026, 11, 11), false, null, Now).Error.ShouldBe(TaskErrors.StartAfterDue);
        task.SetPlanning(_project, null, new DateOnly(2026, 11, 10), false, null, Now).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void UpdateCannotMoveTheDueDateBeforeTheStart()
    {
        var task = NewTask();
        task.SetPlanning(_project, null, new DateOnly(2026, 11, 10), false, null, Now);

        task.Update(_project, "Design", null, TaskPriority.Medium, OwnerId, new DateOnly(2026, 11, 9), Now).Error.ShouldBe(TaskErrors.StartAfterDue);
        task.Update(_project, "Design", null, TaskPriority.Medium, OwnerId, null, Now).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void SetScheduleChangesBothDatesTogether()
    {
        var task = NewTask(new DateOnly(2026, 12, 20));
        task.SetSchedule(_project, new DateOnly(2026, 12, 10), new DateOnly(2026, 12, 20), Now).IsSuccess.ShouldBeTrue();

        // Moving both earlier would trip over the old dates if they were set one by one.
        task.SetSchedule(_project, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 5), Now).IsSuccess.ShouldBeTrue();
        task.SetSchedule(_project, new DateOnly(2026, 11, 6), new DateOnly(2026, 11, 5), Now).Error.ShouldBe(TaskErrors.StartAfterDue);

        task.StartDate.ShouldBe(new DateOnly(2026, 11, 1));
        task.DueDate.ShouldBe(new DateOnly(2026, 11, 5));
    }

    [Fact]
    public void ArchivedProjectIsReadOnly()
    {
        var task = NewTask();
        _project.Archive(Now);

        task.SetPlanning(_project, "A", null, false, null, Now).Error.ShouldBe(ProjectErrors.Archived);
        task.SetDependencies(_project, [], [], Now).Error.ShouldBe(ProjectErrors.Archived);
    }

    [Fact]
    public void DependenciesAreReplacedAndDuplicatesIgnored()
    {
        var (a, b, c) = (NewTask(), NewTask(), NewTask());

        a.SetDependencies(_project, [b, c, b], [], Now).IsSuccess.ShouldBeTrue();
        a.Dependencies.Select(d => d.DependsOnTaskId).ShouldBe([b.Id, c.Id], ignoreOrder: true);

        a.SetDependencies(_project, [c], [], Now.AddHours(1)).IsSuccess.ShouldBeTrue();
        a.Dependencies.Select(d => d.DependsOnTaskId).ShouldBe([c.Id]);
        a.UpdatedAt.ShouldBe(Now.AddHours(1));
    }

    [Fact]
    public void SettingTheSameDependenciesDoesNotTouchTheTask()
    {
        var (a, b) = (NewTask(), NewTask());
        a.SetDependencies(_project, [b], [], Now);

        a.SetDependencies(_project, [b], [], Now.AddHours(1));

        a.UpdatedAt.ShouldBe(Now);
    }

    [Fact]
    public void ATaskCannotDependOnItself()
    {
        var a = NewTask();

        a.SetDependencies(_project, [a], [], Now).Error.ShouldBe(TaskErrors.DependencyOnSelf);
    }

    [Fact]
    public void DependenciesMustBeInTheSameProject()
    {
        var other = Project.Create(Client.Create("Other", null, null, Now), "Other", null, OwnerId, Now).Value;
        var foreign = TaskItem.Create(other, "Elsewhere", null, TaskPriority.Medium, null, null, OwnerId, 0, Now).Value;

        NewTask().SetDependencies(_project, [foreign], [], Now).Error.ShouldBe(TaskErrors.DependencyNotInProject);
    }

    [Fact]
    public void ACycleIsRejected()
    {
        var (a, b, c) = (NewTask(), NewTask(), NewTask());
        // b depends on c, c depends on a: making a depend on b closes a -> b -> c -> a.
        var edges = new[] { (b.Id, c.Id), (c.Id, a.Id) };

        a.SetDependencies(_project, [b], edges, Now).Error.ShouldBe(TaskErrors.DependencyCycle);
        a.Dependencies.ShouldBeEmpty();
    }

    [Fact]
    public void TheTasksOwnOldEdgesAreIgnoredWhenCheckingForCycles()
    {
        var (a, b) = (NewTask(), NewTask());
        // a currently depends on b; replacing that by "b depends on a" direction is checked on b, not here.
        var edges = new[] { (a.Id, b.Id) };

        a.SetDependencies(_project, [b], edges, Now).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void ADiamondIsNotACycle()
    {
        var (a, b, c, d) = (NewTask(), NewTask(), NewTask(), NewTask());
        var edges = new[] { (b.Id, d.Id), (c.Id, d.Id) };

        a.SetDependencies(_project, [b, c], edges, Now).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void ATaskHasAtMostFiftyDependencies()
    {
        var a = NewTask();
        var many = Enumerable.Range(0, TaskItem.MaxDependencies + 1).Select(_ => NewTask()).ToList();

        a.SetDependencies(_project, many, [], Now).Error.ShouldBe(TaskErrors.TooManyDependencies);
        a.SetDependencies(_project, many.Take(TaskItem.MaxDependencies).ToList(), [], Now).IsSuccess.ShouldBeTrue();
    }
}
