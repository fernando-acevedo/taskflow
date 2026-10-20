using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Projects;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Tasks;

public sealed record DeleteTaskCommand(Guid ProjectId, Guid TaskId);

/// <summary>Any member deletes a task. The gap it leaves in its column is harmless: the board orders by position.</summary>
public sealed class DeleteTaskHandler(IAppDbContext db, ProjectAccess access)
{
    public async Task<Result> HandleAsync(DeleteTaskCommand command, CancellationToken cancellationToken)
    {
        var allowed = await access.GetAsync(command.ProjectId, ProjectAccessLevel.Work, cancellationToken);
        if (!allowed.IsSuccess)
        {
            return allowed;
        }

        var editable = allowed.Value.CanChangeTask(assigneeId: null);
        if (!editable.IsSuccess)
        {
            return editable;
        }

        var task = await db.Tasks.FirstOrDefaultAsync(
            task => task.Id == command.TaskId && task.ProjectId == command.ProjectId, cancellationToken);
        if (task is null)
        {
            return TaskErrors.NotFound;
        }

        db.Tasks.Remove(task);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
