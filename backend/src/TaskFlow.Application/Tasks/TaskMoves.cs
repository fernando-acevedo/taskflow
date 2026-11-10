using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Activity;
using TaskFlow.Domain.Activity;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Tasks;

/// <summary>
/// The Kanban move shared by <see cref="MoveTaskHandler"/> and the task upserts (ADR 0017): puts the task at a position of
/// a column, renumbers the columns it left and entered, and records the activity. It does not save. <paramref name="repositioned"/> receives the other tasks whose position changed.
/// </summary>
internal static class TaskMoves
{
    /// <param name="position">Index in the target column, counted without the moved task; past the end means last.</param>
    public static async Task<Result> MoveAsync(
        IAppDbContext db,
        ActivityLog activity,
        TimeProvider time,
        Project project,
        TaskItem task,
        TaskItemStatus status,
        int position,
        CancellationToken cancellationToken,
        List<TaskItem>? repositioned = null)
    {
        var sourceStatus = task.Status;
        var target = await ColumnWithoutAsync(db, task, status, cancellationToken);
        var index = Math.Min(position, target.Count);

        var moved = task.Move(project, status, index, time.GetUtcNow());
        if (!moved.IsSuccess)
        {
            return moved;
        }

        if (sourceStatus != status)
        {
            activity.Record(
                project.Id,
                ActivityType.TaskMoved,
                new { taskTitle = task.Title, from = sourceStatus, to = status },
                task.Id);
        }

        target.Insert(index, task);
        Renumber(target, task, repositioned);
        if (sourceStatus != status)
        {
            Renumber(await ColumnWithoutAsync(db, task, sourceStatus, cancellationToken), task, repositioned);
        }

        return Result.Success();
    }

    private static Task<List<TaskItem>> ColumnWithoutAsync(IAppDbContext db, TaskItem task, TaskItemStatus status, CancellationToken cancellationToken) =>
        db.Tasks
            .Where(other => other.ProjectId == task.ProjectId && other.Status == status && other.Id != task.Id)
            .OrderBy(other => other.Position)
            .ToListAsync(cancellationToken);

    private static void Renumber(List<TaskItem> column, TaskItem moved, List<TaskItem>? repositioned)
    {
        for (var index = 0; index < column.Count; index++)
        {
            if (column[index].Position != index)
            {
                column[index].Reposition(index);
                if (column[index] != moved)
                {
                    repositioned?.Add(column[index]);
                }
            }
        }
    }
}
