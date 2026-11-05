using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Projects;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Application.Labels;

public sealed record DeleteLabelCommand(Guid ProjectId, Guid LabelId);

/// <summary>An owner deletes a label; the database drops it from the tasks that carried it.</summary>
public sealed class DeleteLabelHandler(IAppDbContext db, ProjectAccess access)
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

        var deletable = Label.CanDelete(allowed.Value);
        if (!deletable.IsSuccess)
        {
            return deletable;
        }

        db.Labels.Remove(label);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
