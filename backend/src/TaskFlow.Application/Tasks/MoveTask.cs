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

/// <summary>A task whose position changed because of a move; the row version is the one saved.</summary>
public sealed record TaskPositionDto(Guid Id, TaskItemStatus Status, int Position, byte[] RowVersion);

/// <summary>The moved task plus the other tasks the renumbering touched, so the client needs no reload.</summary>
public sealed record MoveTaskResultDto(TaskDto Task, IReadOnlyList<TaskPositionDto> Reordered);

/// <summary>A Kanban drag and drop: moves the task and renumbers the columns it left and entered.</summary>
public sealed class MoveTaskHandler(
    IAppDbContext db,
    ProjectAccess access,
    IValidator<MoveTaskCommand> validator,
    GetTaskHandler getTask,
    ActivityLog activity,
    ITransactionRunner transactions,
    TimeProvider time)
{
    public async Task<Result<MoveTaskResultDto>> HandleAsync(MoveTaskCommand command, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        var reordered = new List<TaskPositionDto>();

        // Serializable: two concurrent moves in a column can't both renumber it from the same snapshot.
        var result = await transactions.RunSerializableAsync(async token =>
        {
            reordered.Clear();
            var allowed = await access.GetAsync(command.ProjectId, ProjectAccessLevel.Work, token);
            if (!allowed.IsSuccess)
            {
                return allowed;
            }

            var task = await db.Tasks.FirstOrDefaultAsync(
                task => task.Id == command.TaskId && task.ProjectId == command.ProjectId, token);
            if (task is null)
            {
                return TaskErrors.NotFound;
            }

            if (!task.RowVersion.SequenceEqual(command.RowVersion))
            {
                return CommonErrors.Stale;
            }

            var changed = new List<TaskItem>();
            var moved = await TaskMoves.MoveAsync(db, activity, time, allowed.Value, task, command.Status, command.Position, token, changed);
            if (!moved.IsSuccess)
            {
                return moved;
            }

            await db.SaveChangesAsync(token);
            reordered.AddRange(changed.Select(other => new TaskPositionDto(other.Id, other.Status, other.Position, other.RowVersion)));
            return Result.Success();
        }, cancellationToken);

        if (!result.IsSuccess)
        {
            return result.Error;
        }

        var dto = await getTask.HandleAsync(new GetTaskQuery(command.ProjectId, command.TaskId), cancellationToken);
        return dto.IsSuccess ? new MoveTaskResultDto(dto.Value, reordered) : dto.Error;
    }
}
