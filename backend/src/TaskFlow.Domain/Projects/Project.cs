using TaskFlow.Domain.Clients;
using TaskFlow.Domain.Common;

namespace TaskFlow.Domain.Projects;

/// <summary>
/// Aggregate root for a client project and its members. Membership changes go through its methods, which
/// keep the rules: at least one owner, and no changes while archived.
/// </summary>
public sealed class Project
{
    public const int NameMinLength = 3;
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 2000;

    private readonly List<ProjectMember> _members = [];

    private Project(Guid id, Guid clientId, string name, string? description, DateTimeOffset createdAt)
    {
        Id = id;
        ClientId = clientId;
        Name = name;
        Description = description;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid ClientId { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public bool IsArchived { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token, maintained by the database.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<ProjectMember> Members => _members;

    /// <summary>Creates a project for an active client, with its creator as the first owner.</summary>
    public static Result<Project> Create(Client client, string name, string? description, Guid ownerId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (client.IsArchived)
        {
            return ClientErrors.Archived;
        }

        var project = new Project(Guid.CreateVersion7(now), client.Id, name, description, now);
        project._members.Add(new ProjectMember(project.Id, ownerId, ProjectRole.Owner, now));
        return project;
    }

    public ProjectRole? RoleOf(Guid userId) => _members.FirstOrDefault(member => member.UserId == userId)?.Role;

    public bool IsMember(Guid userId) => RoleOf(userId) is not null;

    /// <summary>Renames the project or moves it to another client; moving requires that client to be active.</summary>
    public Result Update(Client client, string name, string? description, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (IsArchived)
        {
            return ProjectErrors.Archived;
        }

        if (client.Id != ClientId && client.IsArchived)
        {
            return ClientErrors.Archived;
        }

        ClientId = client.Id;
        Name = name;
        Description = description;
        UpdatedAt = now;
        return Result.Success();
    }

    public void Archive(DateTimeOffset now) => SetArchived(true, now);

    /// <summary>Makes the project editable again; its client must be active.</summary>
    public Result Restore(Client client, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (client.Id != ClientId)
        {
            throw new ArgumentException("That is not the project's client.", nameof(client));
        }

        if (client.IsArchived)
        {
            return ClientErrors.Archived;
        }

        SetArchived(false, now);
        return Result.Success();
    }

    public Result AddMember(Guid userId, ProjectRole role, DateTimeOffset now)
    {
        if (IsArchived)
        {
            return ProjectErrors.Archived;
        }

        if (IsMember(userId))
        {
            return ProjectErrors.AlreadyMember;
        }

        _members.Add(new ProjectMember(Id, userId, role, now));
        UpdatedAt = now;
        return Result.Success();
    }

    public Result ChangeMemberRole(Guid userId, ProjectRole role, DateTimeOffset now)
    {
        var check = CheckMemberChange(userId, leavesOwnerRole: role != ProjectRole.Owner);
        if (!check.IsSuccess)
        {
            return check;
        }

        _members.Single(member => member.UserId == userId).Role = role;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result RemoveMember(Guid userId, DateTimeOffset now)
    {
        var check = CheckMemberChange(userId, leavesOwnerRole: true);
        if (!check.IsSuccess)
        {
            return check;
        }

        _members.RemoveAll(member => member.UserId == userId);
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>Checks that tasks can change in this project and that the assignee, if any, is a member.</summary>
    public Result CanChangeTask(Guid? assigneeId)
    {
        if (IsArchived)
        {
            return ProjectErrors.Archived;
        }

        if (assigneeId is { } id && !IsMember(id))
        {
            return ProjectErrors.AssigneeNotMember;
        }

        return Result.Success();
    }

    private Result CheckMemberChange(Guid userId, bool leavesOwnerRole)
    {
        if (IsArchived)
        {
            return ProjectErrors.Archived;
        }

        var role = RoleOf(userId);
        if (role is null)
        {
            return ProjectErrors.MemberNotFound;
        }

        if (leavesOwnerRole && role == ProjectRole.Owner && _members.Count(member => member.Role == ProjectRole.Owner) == 1)
        {
            return ProjectErrors.LastOwner;
        }

        return Result.Success();
    }

    private void SetArchived(bool isArchived, DateTimeOffset now)
    {
        if (IsArchived != isArchived)
        {
            IsArchived = isArchived;
            UpdatedAt = now;
        }
    }
}

public static class ProjectErrors
{
    public static readonly Error NotFound =
        Error.NotFound("Projects.NotFound", "The project was not found.");

    public static readonly Error Forbidden =
        Error.Forbidden("Projects.Forbidden", "You don't have permission to do this in the project.");

    public static readonly Error NameTaken =
        Error.Conflict("Projects.NameTaken", "The client already has a project with this name.");

    public static readonly Error Archived =
        Error.Conflict("Projects.Archived", "The project is archived and read-only.");

    public static readonly Error AlreadyMember =
        Error.Conflict("Projects.AlreadyMember", "The user is already a member of the project.");

    public static readonly Error MemberNotFound =
        Error.NotFound("Projects.MemberNotFound", "The user is not a member of the project.");

    public static readonly Error LastOwner =
        Error.Conflict("Projects.LastOwner", "A project needs at least one owner.");

    public static readonly Error AssigneeNotMember =
        Error.Invalid("Projects.AssigneeNotMember", "The assignee must be a member of the project.");
}
