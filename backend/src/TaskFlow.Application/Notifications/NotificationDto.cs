using System.Text.Json;
using TaskFlow.Domain.Notifications;

namespace TaskFlow.Application.Notifications;

/// <param name="Id">The notification's id.</param>
/// <param name="Type">What happened.</param>
/// <param name="ProjectId">The project it belongs to.</param>
/// <param name="TaskId">The task it is about; may name a task that has since been deleted.</param>
/// <param name="ActorId">Who caused it; null when the system did (a task due soon).</param>
/// <param name="Data">A JSON object whose keys depend on <paramref name="Type"/>; see ADR 0010.</param>
/// <param name="CreatedAt">When it was raised.</param>
/// <param name="ReadAt">When the user read it; null while unread.</param>
public sealed record NotificationDto(
    Guid Id,
    NotificationType Type,
    Guid ProjectId,
    Guid? TaskId,
    Guid? ActorId,
    JsonElement Data,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt)
{
    public static NotificationDto From(Notification notification) => new(
        notification.Id,
        notification.Type,
        notification.ProjectId,
        notification.TaskItemId,
        notification.ActorId,
        JsonSerializer.Deserialize<JsonElement>(notification.Data),
        notification.CreatedAt,
        notification.ReadAt);
}

public sealed record UnreadCountDto(int Count);

public sealed record NotificationPreferencesDto(
    bool EmailOnAssigned,
    bool EmailOnMentioned,
    bool EmailOnCommentOnMyTask,
    bool EmailDailyDigest)
{
    public static NotificationPreferencesDto From(NotificationPreferences? preferences) => preferences is null
        ? new NotificationPreferencesDto(false, false, false, false)
        : new NotificationPreferencesDto(
            preferences.EmailOnAssigned,
            preferences.EmailOnMentioned,
            preferences.EmailOnCommentOnMyTask,
            preferences.EmailDailyDigest);
}
