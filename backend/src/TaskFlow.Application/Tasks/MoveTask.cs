using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Activity;
using TaskFlow.Application.Common;
using TaskFlow.Application.Projects;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Tasks;

/// <param name="Position">Index in the target column, counted without the moved task; past the end means last.</param>
public sealed record MoveTaskCommand(Guid ProjectId, Guid TaskId, TaskItemStatus Status, int Position, byte[] RowVersion);

public sealed class MoveTaskValidator : AbstractValidator<MoveTaskCommand>
{
    public MoveTaskValidator()
    {
        RuleFor(command => command.Status).IsInEnum();
        RuleFor(command => command.Position).GreaterThanOrEqualTo(0);
        RuleFor(command => command.RowVersion).NotEmpty();
    }
}

/// <summary>A Kanban drag and drop: moves the task and renumbers the columns it left and entered.</summary>
public sealed class MoveTaskHandler(
    IAppDbContext db,
    ProjectAccess access,
    IValidator<MoveTaskCommand> validator,
    GetTaskHandler getTask,
    ActivityLog activity,
    TimeProvider time)
{
    public async Task<Result<TaskDto>> HandleAsync(MoveTaskCommand command, CancellationToken cancellationToken)
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

        var moved = await TaskMoves.MoveAsync(db, activity, time, allowed.Value, task, command.Status, command.Position, cancellationToken);
        if (!moved.IsSuccess)
        {
            return moved.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await getTask.HandleAsync(new GetTaskQuery(command.ProjectId, task.Id), cancellationToken);
    }
}
