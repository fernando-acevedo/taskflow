using TaskFlow.Application.Users;
using TaskFlow.Domain.Notifications;

namespace TaskFlow.Application.Notifications;

/// <summary>
/// Queues the email of notifications to a user in the outbox (ADR 0018), implemented in Infrastructure. It only adds
/// the row to the context: the caller's own <c>SaveChangesAsync</c> stores it with the change that raised the
/// notification, and a background service sends it later. One call is one email: a single event notification, or
/// several notifications (the daily digest of tasks due soon). It must not throw: an email that can't be composed is
/// logged by the implementation and never undoes the operation that raised the notification.
/// </summary>
public interface INotificationEmailSender
{
    void Enqueue(UserDto recipient, IReadOnlyList<Notification> notifications);
}
