using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Domain.Clients;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Clients;

public sealed record UpdateClientCommand(Guid ClientId, string Name, string? ContactEmail, string? Notes) : IClientFields;

public sealed class UpdateClientValidator : AbstractValidator<UpdateClientCommand>
{
    public UpdateClientValidator() => this.AddClientFieldRules();
}

public sealed class UpdateClientHandler(IAppDbContext db, IValidator<UpdateClientCommand> validator)
{
    public async Task<Result> HandleAsync(UpdateClientCommand command, CancellationToken cancellationToken)
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

        var name = command.Name.Trim();
        if (await db.Clients.AnyAsync(other => other.Id != client.Id && other.Name == name, cancellationToken))
        {
            return ClientErrors.NameTaken;
        }

        client.Update(name, command.ContactEmail.TrimToNull(), command.Notes.TrimToNull());
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
