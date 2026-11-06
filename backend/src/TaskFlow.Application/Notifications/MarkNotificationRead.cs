using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Notifications;

namespace TaskFlow.Application.Notifications;

public sealed record MarkNotificationReadCommand(Guid NotificationId);

/// <summary>Marks a notification of the current user as read; one that belongs to someone else is reported as not found.</summary>
public sealed class MarkNotificationReadHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider time)
{
    public async Task<Result> HandleAsync(MarkNotificationReadCommand command, CancellationToken cancellationToken)
    {
        var notification = await db.Notifications.FirstOrDefaultAsync(
            notification => notification.Id == command.NotificationId && notification.UserId == currentUser.Id, cancellationToken);
        if (notification is null)
        {
            return NotificationErrors.NotFound;
        }

        notification.MarkRead(time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
