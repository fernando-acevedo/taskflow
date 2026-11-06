using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Notifications;

public sealed class MarkAllNotificationsReadHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider time)
{
    public async Task<Result> HandleAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await db.Notifications
            .Where(notification => notification.UserId == currentUser.Id && notification.ReadAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(notification => notification.ReadAt, now), cancellationToken);
        return Result.Success();
    }
}
