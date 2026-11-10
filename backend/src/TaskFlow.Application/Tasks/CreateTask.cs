using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Activity;
using TaskFlow.Application.Common;
using TaskFlow.Application.Notifications;
using TaskFlow.Application.Projects;
using TaskFlow.Domain.Activity;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Notifications;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Tasks;

public sealed record CreateTaskCommand(
    Guid ProjectId,
    string Title,
    string? Description,
    TaskPriority Priority = TaskPriority.Medium,
    Guid? AssigneeId = null,
    DateOnly? DueDate = null,
    int? EstimatedMinutes = null,
    IReadOnlyList<Guid>? LabelIds = null) : ITaskFields;

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
    ActivityLog activity,
    Notifier notifier,
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
            time.GetUtcNow(),
            command.EstimatedMinutes);
        if (!created.IsSuccess)
        {
            return created.Error;
        }

        var labelled = await created.Value.ApplyLabelsAsync(db, allowed.Value, command.LabelIds, cancellationToken);
        if (!labelled.IsSuccess)
        {
            return labelled.Error;
        }

        var task = created.Value;
        activity.Record(
            command.ProjectId,
            ActivityType.TaskCreated,
            new { taskTitle = task.Title, status = task.Status, priority = task.Priority, assigneeId = task.AssigneeId },
            task.Id);
        if (task.AssigneeId is { } assigneeId)
        {
            await notifier.NotifyAsync(
                NotificationType.TaskAssigned, assigneeId, command.ProjectId, allowed.Value.Name, task.Id, task.Title, null, cancellationToken);
        }

        db.Tasks.Add(task);
        await db.SaveChangesAsync(cancellationToken);

        return await getTask.HandleAsync(new GetTaskQuery(command.ProjectId, created.Value.Id), cancellationToken);
    }
}
