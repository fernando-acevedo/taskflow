using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Domain.Clients;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Clients;

public sealed record CreateClientCommand(string Name, string? ContactEmail, string? Notes) : IClientFields;

public sealed class CreateClientValidator : AbstractValidator<CreateClientCommand>
{
    public CreateClientValidator() => this.AddClientFieldRules();
}

public sealed class CreateClientHandler(
    IAppDbContext db,
    IValidator<CreateClientCommand> validator,
    GetClientHandler getClient,
    TimeProvider time)
{
    public async Task<Result<ClientDto>> HandleAsync(CreateClientCommand command, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        var name = command.Name.Trim();
        if (await db.Clients.AnyAsync(client => client.Name == name, cancellationToken))
        {
            return ClientErrors.NameTaken;
        }

        var client = Client.Create(name, command.ContactEmail.TrimToNull(), command.Notes.TrimToNull(), time.GetUtcNow());
        db.Clients.Add(client);
        await db.SaveChangesAsync(cancellationToken);

        return await getClient.HandleAsync(client.Id, cancellationToken);
    }
}
