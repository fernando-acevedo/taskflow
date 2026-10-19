namespace TaskFlow.Domain.Projects;

public enum ProjectRole
{
    Member,
    Owner,
}

/// <summary>A user's membership in a project. Changed only through <see cref="Project"/>.</summary>
public sealed class ProjectMember
{
    internal ProjectMember(Guid projectId, Guid userId, ProjectRole role, DateTimeOffset joinedAt)
    {
        ProjectId = projectId;
        UserId = userId;
        Role = role;
        JoinedAt = joinedAt;
    }

    public Guid ProjectId { get; private set; }

    public Guid UserId { get; private set; }

    public ProjectRole Role { get; internal set; }

    public DateTimeOffset JoinedAt { get; private set; }
}
