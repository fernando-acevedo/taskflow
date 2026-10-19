using TaskFlow.Domain.Clients;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Domain.UnitTests.Tasks;

public sealed class TaskItemTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid OwnerId = Guid.NewGuid();

    private readonly Project _project =
        Project.Create(Client.Create("Acme", null, null, Now), "Website", null, OwnerId, Now).Value;

    [Fact]
    public void NewTaskStartsInToDo()
    {
        var task = NewTask();

        task.Status.ShouldBe(TaskItemStatus.ToDo);
        task.CompletedAt.ShouldBeNull();
    }

    [Fact]
    public void CannotAssignSomeoneOutsideTheProject()
    {
        var created = TaskItem.Create(_project, "Design", null, TaskPriority.High, Guid.NewGuid(), null, OwnerId, 0, Now);

        created.Error.ShouldBe(ProjectErrors.AssigneeNotMember);
    }

    [Fact]
    public void MovingToDoneSetsCompletedAtAndLeavingClearsIt()
    {
        var task = NewTask();
        var later = Now.AddHours(3);

        task.Move(_project, TaskItemStatus.Done, 0, later).IsSuccess.ShouldBeTrue();
        task.CompletedAt.ShouldBe(later);

        task.Move(_project, TaskItemStatus.Review, 0, later.AddHours(1)).IsSuccess.ShouldBeTrue();
        task.CompletedAt.ShouldBeNull();
    }

    [Fact]
    public void ReorderingWithinDoneKeepsCompletedAt()
    {
        var task = NewTask();
        task.Move(_project, TaskItemStatus.Done, 0, Now);

        task.Move(_project, TaskItemStatus.Done, 3, Now.AddDays(1));

        task.CompletedAt.ShouldBe(Now);
        task.Position.ShouldBe(3);
    }

    [Fact]
    public void FormerMemberCanStayAssignedButNotBeAssignedAgain()
    {
        var memberId = Guid.NewGuid();
        _project.AddMember(memberId, ProjectRole.Member, Now);
        var task = TaskItem.Create(_project, "Design", null, TaskPriority.High, memberId, null, OwnerId, 0, Now).Value;
        _project.RemoveMember(memberId, Now);

        task.Update(_project, "Design v2", null, TaskPriority.High, memberId, null, Now).IsSuccess.ShouldBeTrue();
        task.Update(_project, "Design v2", null, TaskPriority.High, null, null, Now).IsSuccess.ShouldBeTrue();
        task.Update(_project, "Design v2", null, TaskPriority.High, memberId, null, Now).Error.ShouldBe(ProjectErrors.AssigneeNotMember);
    }

    [Fact]
    public void ArchivedProjectRejectsTaskChanges()
    {
        var task = NewTask();
        _project.Archive(Now);

        task.Update(_project, "Renamed", null, TaskPriority.Low, null, null, Now).Error.ShouldBe(ProjectErrors.Archived);
        task.Move(_project, TaskItemStatus.Done, 0, Now).Error.ShouldBe(ProjectErrors.Archived);
    }

    private TaskItem NewTask() =>
        TaskItem.Create(_project, "Design", null, TaskPriority.Medium, OwnerId, null, OwnerId, 0, Now).Value;
}
