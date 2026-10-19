using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Projects;

public sealed record SetProjectArchivedCommand(Guid ProjectId, bool IsArchived);

/// <summary>Archiving is a soft delete: the project becomes read-only and leaves the default list.</summary>
public sealed class SetProjectArchivedHandler(IAppDbContext db, ProjectAccess access, TimeProvider time)
{
    public async Task<Result> HandleAsync(SetProjectArchivedCommand command, CancellationToken cancellationToken)
    {
        var allowed = await access.GetAsync(command.ProjectId, ProjectAccessLevel.Manage, cancellationToken);
        if (!allowed.IsSuccess)
        {
            return allowed;
        }

        var project = allowed.Value;
        if (command.IsArchived)
        {
            project.Archive(time.GetUtcNow());
        }
        else
        {
            var client = await db.Clients.FirstAsync(client => client.Id == project.ClientId, cancellationToken);
            var restored = project.Restore(client, time.GetUtcNow());
            if (!restored.IsSuccess)
            {
                return restored;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
