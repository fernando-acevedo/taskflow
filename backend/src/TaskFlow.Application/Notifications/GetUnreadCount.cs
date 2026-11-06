using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Notifications;

/// <summary>How many notifications the current user has not read yet; the frontend polls this (ADR 0010).</summary>
public sealed class GetUnreadCountHandler(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<Result<UnreadCountDto>> HandleAsync(CancellationToken cancellationToken) =>
        new UnreadCountDto(await db.Notifications.CountAsync(
            notification => notification.UserId == currentUser.Id && notification.ReadAt == null
                && db.Projects.Any(project => project.Id == notification.ProjectId
                    && project.Members.Any(member => member.UserId == currentUser.Id)),
            cancellationToken));
}
