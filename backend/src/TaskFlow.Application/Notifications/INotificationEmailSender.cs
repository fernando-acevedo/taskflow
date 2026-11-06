using TaskFlow.Application.Users;
using TaskFlow.Domain.Notifications;

namespace TaskFlow.Application.Notifications;

/// <summary>
/// Emails notifications to a user, implemented in Infrastructure. One call is one email: a single event
/// notification, or several notifications (the daily digest of tasks due soon). It must not throw: a failed
/// delivery is logged by the implementation and never undoes the operation that raised the notification.
/// </summary>
public interface INotificationEmailSender
{
    Task SendAsync(UserDto recipient, IReadOnlyList<Notification> notifications, CancellationToken cancellationToken);
}
