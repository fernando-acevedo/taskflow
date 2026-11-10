using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Application.Projects;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Tasks;

/// <param name="RowVersion">The version the user edited; a newer one in the database means a conflicting edit.</param>
public sealed record UpdateTaskCommand(
    Guid ProjectId,
    Guid TaskId,
    string Title,
    string? Description,
    TaskPriority Priority,
    Guid? AssigneeId,
    DateOnly? DueDate,
    int? EstimatedMinutes,
    byte[] RowVersion,
    IReadOnlyList<Guid>? LabelIds = null,
    DateOnly? StartDate = null,
    bool IsBlocked = false,
    string? StatusNote = null) : ITaskFields;

/// <summary>Unlike creation, an update may keep a due date that has passed.</summary>
public sealed class UpdateTaskValidator : AbstractValidator<UpdateTaskCommand>
{
    public UpdateTaskValidator()
    {
        this.AddTaskFieldRules();
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.StatusNote).MaximumLength(TaskItem.StatusNoteMaxLength);
    }
}

public sealed class UpdateTaskHandler(
    IAppDbContext db,
    ProjectAccess access,
    IValidator<UpdateTaskCommand> validator,
    GetTaskHandler getTask,
    TimeProvider time)
{
    public async Task<Result<TaskDto>> HandleAsync(UpdateTaskCommand command, CancellationToken cancellationToken)
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

        var task = await db.Tasks.Include(task => task.Labels).FirstOrDefaultAsync(
            task => task.Id == command.TaskId && task.ProjectId == command.ProjectId, cancellationToken);
        if (task is null)
        {
            return TaskErrors.NotFound;
        }

        if (!RowVersions.Matches(task.RowVersion, command.RowVersion))
        {
            return CommonErrors.Stale;
        }

        var now = time.GetUtcNow();
        var updated = task.SetSchedule(allowed.Value, command.StartDate, command.DueDate, now);
        if (updated.IsSuccess)
        {
            updated = task.SetPlanning(allowed.Value, null, command.StartDate, command.IsBlocked, command.StatusNote, now);
        }

        if (updated.IsSuccess)
        {
            updated = task.Update(
                allowed.Value,
                command.Title.Trim(),
                command.Description.TrimToNull(),
                command.Priority,
                command.AssigneeId,
                command.DueDate,
                now,
                command.EstimatedMinutes);
        }

        if (!updated.IsSuccess)
        {
            return updated.Error;
        }

        var labelled = await task.ApplyLabelsAsync(db, allowed.Value, command.LabelIds, cancellationToken);
        if (!labelled.IsSuccess)
        {
            return labelled.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await getTask.HandleAsync(new GetTaskQuery(command.ProjectId, task.Id), cancellationToken);
    }
}
