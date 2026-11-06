using TaskFlow.Domain.Tenancy;
using TaskFlow.Domain.Common;

namespace TaskFlow.Domain.Notifications;

/// <summary>What the notification is about. The name is the contract with the frontend, which builds the sentence from it.</summary>
public enum NotificationType
{
    TaskAssigned,
    Mentioned,
    CommentOnYourTask,
    TaskDueSoon,
}

/// <summary>
/// An in-app notification for one user. <see cref="Data"/> carries the values the text needs (task title, project
/// name, actor name at that moment) because the things they name may be renamed or gone by the time it is read.
/// </summary>
public sealed class Notification : ITenantOwned
{
    private Notification(
        Guid id,
        Guid userId,
        NotificationType type,
        Guid projectId,
        Guid? taskItemId,
        Guid? actorId,
        string data,
        DateTimeOffset createdAt)
    {
        Id = id;
        UserId = userId;
        Type = type;
        ProjectId = projectId;
        TaskItemId = taskItemId;
        ActorId = actorId;
        Data = data;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    /// <summary>The recipient.</summary>
    public Guid UserId { get; private set; }

    public NotificationType Type { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid? TaskItemId { get; private set; }

    /// <summary>Who caused it; null for notifications the system raises (a task due soon).</summary>
    public Guid? ActorId { get; private set; }

    /// <summary>A JSON object whose keys depend on <see cref="Type"/>.</summary>
    public string Data { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public bool IsRead => ReadAt is not null;

    public static Notification Create(
        Guid userId,
        NotificationType type,
        Guid projectId,
        Guid? taskItemId,
        Guid? actorId,
        string dataJson,
        DateTimeOffset now) =>
        new(Guid.CreateVersion7(now), userId, type, projectId, taskItemId, actorId, dataJson, now);

    /// <summary>Marking a read notification again keeps the first time.</summary>
    public void MarkRead(DateTimeOffset now) => ReadAt ??= now;
}

/// <summary>
/// A user's choices about email. In-app notifications are always created; a user without a row has every
/// option off.
/// </summary>
public sealed class NotificationPreferences
{
    private NotificationPreferences(Guid userId) => UserId = userId;

    public Guid UserId { get; private set; }

    public bool EmailOnAssigned { get; private set; }

    public bool EmailOnMentioned { get; private set; }

    public bool EmailOnCommentOnMyTask { get; private set; }

    /// <summary>One email a day listing the tasks that are due today or tomorrow.</summary>
    public bool EmailDailyDigest { get; private set; }

    public static NotificationPreferences CreateDefault(Guid userId) => new(userId);

    public void Update(bool emailOnAssigned, bool emailOnMentioned, bool emailOnCommentOnMyTask, bool emailDailyDigest)
    {
        EmailOnAssigned = emailOnAssigned;
        EmailOnMentioned = emailOnMentioned;
        EmailOnCommentOnMyTask = emailOnCommentOnMyTask;
        EmailDailyDigest = emailDailyDigest;
    }

    /// <summary>Whether the user wants an email for a notification of <paramref name="type"/> (the daily digest is separate).</summary>
    public bool WantsEmailFor(NotificationType type) => type switch
    {
        NotificationType.TaskAssigned => EmailOnAssigned,
        NotificationType.Mentioned => EmailOnMentioned,
        NotificationType.CommentOnYourTask => EmailOnCommentOnMyTask,
        _ => false,
    };
}

public static class NotificationErrors
{
    public static readonly Error NotFound =
        Error.NotFound("Notifications.NotFound", "The notification was not found.");
}
