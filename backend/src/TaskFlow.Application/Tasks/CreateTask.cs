using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Application.Projects;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Tasks;

public sealed record CreateTaskCommand(
    Guid ProjectId,
    string Title,
    string? Description,
    TaskPriority Priority = TaskPriority.Medium,
    Guid? AssigneeId = null,
    DateOnly? DueDate = null) : ITaskFields;

public sealed class CreateTaskValidator : AbstractValidator<CreateTaskCommand>
{
    public CreateTaskValidator(TimeProvider time)
    {
        this.AddTaskFieldRules();
        RuleFor(command => command.DueDate).GreaterThanOrEqualTo(_ => time.Today());
    }
}

/// <summary>Any member adds a task at the bottom of the To Do column.</summary>
public sealed class CreateTaskHandler(
    IAppDbContext db,
    ProjectAccess access,
    ICurrentUser currentUser,
    IValidator<CreateTaskCommand> validator,
    GetTaskHandler getTask,
    TimeProvider time)
{
    public async Task<Result<TaskDto>> HandleAsync(CreateTaskCommand command, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        var allowed = await access.GetAsync(command.ProjectId, ProjectAccessLevel.Work, cancellationToken);
        if (!allowed.IsSuccess)
        {
            return allowed.Error;
        }

        var lastPosition = await db.Tasks
            .Where(task => task.ProjectId == command.ProjectId && task.Status == TaskItemStatus.ToDo)
            .MaxAsync(task => (int?)task.Position, cancellationToken);
        var position = lastPosition + 1 ?? 0;

        var created = TaskItem.Create(
            allowed.Value,
            command.Title.Trim(),
            command.Description.TrimToNull(),
            command.Priority,
            command.AssigneeId,
            command.DueDate,
            currentUser.Id,
            position,
            time.GetUtcNow());
        if (!created.IsSuccess)
        {
            return created.Error;
        }

        db.Tasks.Add(created.Value);
        await db.SaveChangesAsync(cancellationToken);

        return await getTask.HandleAsync(new GetTaskQuery(command.ProjectId, created.Value.Id), cancellationToken);
    }
}
