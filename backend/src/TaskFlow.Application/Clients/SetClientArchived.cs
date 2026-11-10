using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Domain.Clients;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Clients;

public sealed record SetClientArchivedCommand(Guid ClientId, bool IsArchived, byte[] RowVersion);

public sealed class SetClientArchivedValidator : AbstractValidator<SetClientArchivedCommand>
{
    public SetClientArchivedValidator() => RuleFor(command => command.RowVersion).NotEmpty();
}

public sealed class SetClientArchivedHandler(IAppDbContext db, IValidator<SetClientArchivedCommand> validator)
{
    public async Task<Result> HandleAsync(SetClientArchivedCommand command, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        var client = await db.Clients.FirstOrDefaultAsync(client => client.Id == command.ClientId, cancellationToken);
        if (client is null)
        {
            return ClientErrors.NotFound;
        }

        if (!RowVersions.Matches(client.RowVersion, command.RowVersion))
        {
            return CommonErrors.Stale;
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
