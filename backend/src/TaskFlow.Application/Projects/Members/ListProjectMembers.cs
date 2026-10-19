using TaskFlow.Application.Users;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Application.Projects.Members;

public sealed record ProjectMemberDto(
    Guid UserId,
    string Email,
    string DisplayName,
    bool IsActive,
    ProjectRole Role,
    DateTimeOffset JoinedAt);

/// <summary>Members with their user details, owners first. Users live in Identity, so this joins in memory.</summary>
public sealed class ListProjectMembersHandler(ProjectAccess access, IIdentityService identity)
{
    public async Task<Result<IReadOnlyList<ProjectMemberDto>>> HandleAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var allowed = await access.GetAsync(projectId, ProjectAccessLevel.View, cancellationToken);
        if (!allowed.IsSuccess)
        {
            return allowed.Error;
        }

        var members = allowed.Value.Members;
        var users = (await identity.FindByIdsAsync([.. members.Select(member => member.UserId)], cancellationToken))
            .ToDictionary(user => user.Id);

        return members
            .Where(member => users.ContainsKey(member.UserId))
            .Select(member =>
            {
                var user = users[member.UserId];
                return new ProjectMemberDto(user.Id, user.Email, user.DisplayName, user.IsActive, member.Role, member.JoinedAt);
            })
            .OrderByDescending(member => member.Role)
            .ThenBy(member => member.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
