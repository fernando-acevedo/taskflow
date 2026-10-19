using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Projects.Members;

public sealed record RemoveProjectMemberCommand(Guid ProjectId, Guid UserId);

/// <summary>An owner removes a member, or a member leaves. Their tasks keep them as assignee.</summary>
public sealed class RemoveProjectMemberHandler(IAppDbContext db, ProjectAccess access, ICurrentUser currentUser, TimeProvider time)
{
    public async Task<Result> HandleAsync(RemoveProjectMemberCommand command, CancellationToken cancellationToken)
    {
        var level = command.UserId == currentUser.Id ? ProjectAccessLevel.Work : ProjectAccessLevel.Manage;
        var allowed = await access.GetAsync(command.ProjectId, level, cancellationToken);
        if (!allowed.IsSuccess)
        {
            return allowed;
        }

        var removed = allowed.Value.RemoveMember(command.UserId, time.GetUtcNow());
        if (!removed.IsSuccess)
        {
            return removed;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
