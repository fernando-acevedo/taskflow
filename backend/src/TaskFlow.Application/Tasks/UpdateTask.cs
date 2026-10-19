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
    byte[] RowVersion) : ITaskFields;

/// <summary>Unlike creation, an update may keep a due date that has passed.</summary>
public sealed class UpdateTaskValidator : AbstractValidator<UpdateTaskCommand>
{
    public UpdateTaskValidator()
    {
        this.AddTaskFieldRules();
        RuleFor(command => command.RowVersion).NotEmpty();
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

        var task = await db.Tasks.FirstOrDefaultAsync(
            task => task.Id == command.TaskId && task.ProjectId == command.ProjectId, cancellationToken);
        if (task is null)
        {
            return TaskErrors.NotFound;
        }

        if (!task.RowVersion.SequenceEqual(command.RowVersion))
        {
            return CommonErrors.Stale;
        }

        var updated = task.Update(
            allowed.Value,
            command.Title.Trim(),
            command.Description.TrimToNull(),
            command.Priority,
            command.AssigneeId,
            command.DueDate,
            time.GetUtcNow());
        if (!updated.IsSuccess)
        {
            return updated.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await getTask.HandleAsync(new GetTaskQuery(command.ProjectId, task.Id), cancellationToken);
    }
}
