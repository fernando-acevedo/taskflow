using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Activity;
using TaskFlow.Application.Projects;
using TaskFlow.Domain.Activity;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Application.Labels;

public sealed record DeleteLabelCommand(Guid ProjectId, Guid LabelId, byte[]? RowVersion = null);

/// <summary>An owner deletes a label; the database drops it from the tasks that carried it.</summary>
public sealed class DeleteLabelHandler(IAppDbContext db, ProjectAccess access, ActivityLog activity)
{
    public async Task<Result> HandleAsync(DeleteLabelCommand command, CancellationToken cancellationToken)
    {
        var allowed = await access.GetAsync(command.ProjectId, ProjectAccessLevel.Manage, cancellationToken);
        if (!allowed.IsSuccess)
        {
            return allowed.Error;
        }

        var label = await db.Labels.FirstOrDefaultAsync(
            label => label.Id == command.LabelId && label.ProjectId == command.ProjectId, cancellationToken);
        if (label is null)
        {
            return LabelErrors.NotFound;
        }

        if (command.RowVersion is not null && !RowVersions.Matches(label.RowVersion, command.RowVersion))
        {
            return CommonErrors.Stale;
        }

        var deletable = Label.CanDelete(allowed.Value);
        if (!deletable.IsSuccess)
        {
            return deletable;
        }

        activity.Record(command.ProjectId, ActivityType.LabelDeleted, new { labelId = label.Id, labelName = label.Name });
        db.Labels.Remove(label);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
