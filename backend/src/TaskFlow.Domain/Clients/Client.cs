using TaskFlow.Domain.Common;

namespace TaskFlow.Domain.Clients;

/// <summary>A customer of the agency. Every project belongs to one; only admins manage them.</summary>
public sealed class Client
{
    public const int NameMaxLength = 100;
    public const int ContactEmailMaxLength = 256;
    public const int NotesMaxLength = 2000;

    private Client(Guid id, string name, string? contactEmail, string? notes, DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        ContactEmail = contactEmail;
        Notes = notes;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public string? ContactEmail { get; private set; }

    public string? Notes { get; private set; }

    public bool IsArchived { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Client Create(string name, string? contactEmail, string? notes, DateTimeOffset now) =>
        new(Guid.CreateVersion7(now), name, contactEmail, notes, now);

    public void Update(string name, string? contactEmail, string? notes)
    {
        Name = name;
        ContactEmail = contactEmail;
        Notes = notes;
    }

    /// <summary>Archives the client; the caller counts its active projects, which live in another aggregate.</summary>
    public Result Archive(int activeProjectCount)
    {
        if (activeProjectCount > 0)
        {
            return ClientErrors.HasActiveProjects;
        }

        IsArchived = true;
        return Result.Success();
    }

    public void Restore() => IsArchived = false;
}

public static class ClientErrors
{
    public static readonly Error NotFound =
        Error.NotFound("Clients.NotFound", "The client was not found.");

    public static readonly Error NameTaken =
        Error.Conflict("Clients.NameTaken", "Another client already has this name.");

    public static readonly Error HasActiveProjects =
        Error.Conflict("Clients.HasActiveProjects", "A client with active projects can't be archived.");

    public static readonly Error Archived =
        Error.Conflict("Clients.Archived", "The client is archived; restore it before adding projects.");
}
