using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Clients;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Clients;

public sealed record SetClientArchivedCommand(Guid ClientId, bool IsArchived);

public sealed class SetClientArchivedHandler(IAppDbContext db)
{
    public async Task<Result> HandleAsync(SetClientArchivedCommand command, CancellationToken cancellationToken)
    {
        var client = await db.Clients.FirstOrDefaultAsync(client => client.Id == command.ClientId, cancellationToken);
        if (client is null)
        {
            return ClientErrors.NotFound;
        }

        if (command.IsArchived)
        {
            var activeProjects = await db.Projects.CountAsync(
                project => project.ClientId == client.Id && !project.IsArchived, cancellationToken);
            var archived = client.Archive(activeProjects);
            if (!archived.IsSuccess)
            {
                return archived;
            }
        }
        else
        {
            client.Restore();
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
