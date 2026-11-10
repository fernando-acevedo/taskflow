using TaskFlow.Domain.Clients;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Domain.UnitTests.Tasks;

public sealed class DomainEventsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();

    private readonly Project _project;

    public DomainEventsTests()
    {
        _project = Project.Create(Client.Create("Acme", null, null, Now), "Website", null, OwnerId, Now).Value;
        _project.AddMember(MemberId, ProjectRole.Member, Now);
    }

    [Fact]
    public void CreatingATaskRaisesTaskCreatedOnceAndNoUpdate()
    {
        var task = TaskItem.Create(_project, "Design", null, TaskPriority.High, MemberId, null, OwnerId, 0, Now).Value;
        task.SetPlanning(_project, null, null, true, "waiting", Now);

        var created = task.TakeDomainEvents().ShouldHaveSingleItem().ShouldBeOfType<TaskCreated>();

        created.TaskId.ShouldBe(task.Id);
        created.ProjectName.ShouldBe("Website");
        created.AssigneeId.ShouldBe(MemberId);
        task.TakeDomainEvents().ShouldBeEmpty();
    }

    [Fact]
    public void SeveralEditsFoldIntoOneTaskUpdatedWithTheNetChange()
    {
        var task = Persisted();

        task.SetSchedule(_project, null, new DateOnly(2026, 12, 1), Now);
        task.Update(_project, "Design v2", null, TaskPriority.High, MemberId, new DateOnly(2026, 12, 1), Now);
        task.SetPlanning(_project, null, null, true, "stuck", Now);

        var updated = task.TakeDomainEvents().ShouldHaveSingleItem().ShouldBeOfType<TaskUpdated>();

        updated.Title.ShouldBe("Design v2");
        updated.Fields.Title.ShouldBe(new Change<string>("Design", "Design v2"));
        updated.Fields.Priority!.To.ShouldBe(TaskPriority.High);
        updated.Fields.DueDate!.From.ShouldBeNull();
        updated.Fields.IsBlocked!.To.ShouldBeTrue();
        updated.Fields.StatusNoteChanged.ShouldBeTrue();
        updated.Fields.StartDate.ShouldBeNull();
        updated.PreviousAssigneeId.ShouldBeNull();
        updated.AssigneeId.ShouldBe(MemberId);
        updated.AssigneeChanged.ShouldBeTrue();
    }

    [Fact]
    public void AnEditThatChangesNothingRaisesNothing()
    {
        var task = Persisted();

        task.Update(_project, "Design", null, TaskPriority.Low, null, null, Now);

        task.TakeDomainEvents().ShouldBeEmpty();
    }

    [Fact]
    public void MovingRaisesTaskMovedWithBothColumns()
    {
        var task = Persisted();

        task.Move(_project, TaskItemStatus.Done, 0, Now);

        var moved = task.TakeDomainEvents().ShouldHaveSingleItem().ShouldBeOfType<TaskMoved>();
        moved.From.ShouldBe(TaskItemStatus.ToDo);
        moved.To.ShouldBe(TaskItemStatus.Done);
    }

    [Fact]
    public void ReorderedNeighborsAndDeletionAreEvents()
    {
        var task = Persisted();
        var neighbor = Persisted();

        task.MarkNeighborsReordered([neighbor]);
        task.MarkNeighborsReordered([]);
        task.MarkDeleted();

        var events = task.TakeDomainEvents();
        events.Count.ShouldBe(2);
        events[0].ShouldBeOfType<TasksReordered>().Tasks.ShouldBe([neighbor]);
        events[1].ShouldBeOfType<TaskDeleted>().TaskId.ShouldBe(task.Id);
    }

    [Fact]
    public void CommentsRaiseAddedUpdatedAndDeleted()
    {
        var taskId = Guid.NewGuid();
        var comment = TaskComment.Create(_project, taskId, OwnerId, "Hello", [MemberId], Now).Value;
        var added = comment.TakeDomainEvents().ShouldHaveSingleItem().ShouldBeOfType<CommentAdded>();
        added.MentionedUserIds.ShouldBe([MemberId]);
        added.TaskId.ShouldBe(taskId);

        comment.Edit(_project, OwnerId, "Hello again", [MemberId, OwnerId], Now);
        var updated = comment.TakeDomainEvents().ShouldHaveSingleItem().ShouldBeOfType<CommentUpdated>();
        updated.NewMentionedUserIds.ShouldBe([OwnerId]);

        comment.MarkDeleted(_project);
        comment.TakeDomainEvents().ShouldHaveSingleItem().ShouldBeOfType<CommentDeleted>().CommentId.ShouldBe(comment.Id);
    }

    /// <summary>A task as if it had been saved: the events of its creation are gone.</summary>
    private TaskItem Persisted()
    {
        var task = TaskItem.Create(_project, "Design", null, TaskPriority.Low, null, null, OwnerId, 0, Now).Value;
        task.TakeDomainEvents();
        return task;
    }
}
