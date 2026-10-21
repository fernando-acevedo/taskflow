using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Application.Projects;

public enum ProjectAccessLevel
{
    /// <summary>Read the project, its members and tasks: members, and admins in read-only mode.</summary>
    View,

    /// <summary>Create, edit and move tasks: any member.</summary>
    Work,

    /// <summary>Edit or archive the project and manage its members: owners.</summary>
    Manage,
}

/// <summary>
/// Loads a project with its members and checks the current user's access level on it. Handlers call this
/// first, so authorization by resource lives in one place and is covered by unit tests (ADR 0006).
/// </summary>
public sealed class ProjectAccess(IAppDbContext db, ICurrentUser currentUser)
{
    /// <summary>
    /// <see cref="ProjectAccessLevel.View"/> is only used by reads, so that project comes untracked; the levels
    /// that allow changes return a tracked project for the handler to modify and save.
    /// </summary>
    public async Task<Result<Project>> GetAsync(Guid projectId, ProjectAccessLevel level, CancellationToken cancellationToken)
    {
        var projects = level == ProjectAccessLevel.View ? db.Projects.AsNoTracking() : db.Projects;
        var project = await projects
            .Include(project => project.Members)
            .FirstOrDefaultAsync(project => project.Id == projectId, cancellationToken);

        if (project is null)
        {
            return ProjectErrors.NotFound;
        }

        return Allows(project, level, currentUser) ? project : ProjectErrors.Forbidden;
    }

    public static bool Allows(Project project, ProjectAccessLevel level, ICurrentUser user)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(user);

        return level switch
        {
            ProjectAccessLevel.View => user.IsAdmin || project.IsMember(user.Id),
            ProjectAccessLevel.Work => project.IsMember(user.Id),
            ProjectAccessLevel.Manage => project.RoleOf(user.Id) == ProjectRole.Owner,
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
        };
    }
}
