using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Projects;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.TimeTracking;

public sealed record DeleteTimeEntryCommand(Guid ProjectId, Guid TaskId, Guid EntryId);

/// <summary>The owner of the entry, or an owner of the project, deletes it.</summary>
public sealed class DeleteTimeEntryHandler(IAppDbContext db, ProjectAccess access, ICurrentUser currentUser)
{
    public async Task<Result> HandleAsync(DeleteTimeEntryCommand command, CancellationToken cancellationToken)
    {
        var allowed = await access.GetAsync(command.ProjectId, ProjectAccessLevel.Work, cancellationToken);
        if (!allowed.IsSuccess)
        {
            return allowed;
        }

        var entry = await db.TimeEntries.FirstOrDefaultAsync(
            entry => entry.Id == command.EntryId
                && entry.TaskItemId == command.TaskId
                && db.Tasks.Any(task => task.Id == entry.TaskItemId && task.ProjectId == command.ProjectId),
            cancellationToken);
        if (entry is null)
        {
            return TimeEntryErrors.NotFound;
        }

        var canManage = ProjectAccess.Allows(allowed.Value, ProjectAccessLevel.Manage, currentUser);
        var deletable = entry.CanChange(allowed.Value, currentUser.Id, canManage);
        if (!deletable.IsSuccess)
        {
            return deletable;
        }

        db.TimeEntries.Remove(entry);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone else deleted the same entry first.
            return TimeEntryErrors.NotFound;
        }

        return Result.Success();
    }
}
