using FluentValidation;
using TaskFlow.Domain.Clients;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Application.Clients;

public sealed record ClientDto(
    Guid Id,
    string Name,
    string? ContactEmail,
    string? Notes,
    bool IsArchived,
    int ActiveProjectCount,
    DateTimeOffset CreatedAt);

/// <summary>The editable fields of a client, shared by create and update.</summary>
public interface IClientFields
{
    string Name { get; }

    string? ContactEmail { get; }

    string? Notes { get; }
}

internal static class ClientRules
{
    public static void AddClientFieldRules<T>(this AbstractValidator<T> validator)
        where T : IClientFields
    {
        validator.RuleFor(fields => fields.Name).NotEmpty().Length(2, Client.NameMaxLength);
        validator.RuleFor(fields => fields.ContactEmail).EmailAddress().MaximumLength(Client.ContactEmailMaxLength);
        validator.RuleFor(fields => fields.Notes).MaximumLength(Client.NotesMaxLength);
    }

    public static IQueryable<ClientDto> ToDtos(this IQueryable<Client> clients, IQueryable<Project> projects) =>
        clients.Select(client => new ClientDto(
            client.Id,
            client.Name,
            client.ContactEmail,
            client.Notes,
            client.IsArchived,
            projects.Count(project => project.ClientId == client.Id && !project.IsArchived),
            client.CreatedAt));
}
