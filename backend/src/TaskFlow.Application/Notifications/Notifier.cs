using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Notifications;

namespace TaskFlow.Application.Notifications;

/// <summary>
/// Handlers call <see cref="NotifyAsync"/> next to the change they make, and their own <c>SaveChangesAsync</c> stores
/// the notification, and the outbox row of its email for the users who asked for one, in the same transaction
/// (ADR 0009, ADR 0010, ADR 0018). The current user is never notified about their own action.
/// </summary>
public sealed class Notifier(
    IAppDbContext db,
    ICurrentUser currentUser,
    IIdentityService identity,
    INotificationEmailSender emails,
    TimeProvider time)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Dictionary<Guid, bool> _members = [];
    private readonly Dictionary<Guid, NotificationPreferences?> _preferences = [];
    private readonly Dictionary<Guid, UserDto?> _recipients = [];
    private string? _actorName;

    /// <param name="commentId">The comment that caused it, for mentions and comments.</param>
    public async Task NotifyAsync(
        NotificationType type,
        Guid recipientId,
        Guid projectId,
        string projectName,
        Guid taskId,
        string taskTitle,
        Guid? commentId,
        CancellationToken cancellationToken)
    {
        if (recipientId == currentUser.Id || !await IsTenantMemberAsync(recipientId, cancellationToken))
        {
            return;
        }

        _actorName ??= (await identity.FindByIdAsync(currentUser.Id, cancellationToken))?.DisplayName ?? string.Empty;
        var data = JsonSerializer.Serialize(new { taskTitle, projectName, actorName = _actorName, commentId }, Json);
        var notification = Notification.Create(recipientId, type, projectId, taskId, currentUser.Id, data, time.GetUtcNow());
        db.Notifications.Add(notification);
        await QueueEmailAsync(notification, cancellationToken);
    }

    /// <summary>Defense in depth: only members of the current tenant are notified (someone removed from it is not).</summary>
    private async Task<bool> IsTenantMemberAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return false;
        }

        if (!_members.TryGetValue(userId, out var isMember))
        {
            isMember = await db.TenantMemberships.AnyAsync(
                membership => membership.TenantId == tenantId && membership.UserId == userId, cancellationToken);
            _members[userId] = isMember;
        }

        return isMember;
    }

    /// <summary>
    /// Queues the email (the outbox row joins the handler's own <c>SaveChangesAsync</c>) when the recipient's
    /// preferences ask for it and the account is active.
    /// </summary>
    private async Task QueueEmailAsync(Notification notification, CancellationToken cancellationToken)
    {
        if (!_preferences.TryGetValue(notification.UserId, out var preferences))
        {
            preferences = await db.NotificationPreferences.AsNoTracking()
                .FirstOrDefaultAsync(preference => preference.UserId == notification.UserId, cancellationToken);
            _preferences[notification.UserId] = preferences;
        }

        if (preferences is null || !preferences.WantsEmailFor(notification.Type))
        {
            return;
        }

        if (!_recipients.TryGetValue(notification.UserId, out var user))
        {
            user = await identity.FindByIdAsync(notification.UserId, cancellationToken);
            _recipients[notification.UserId] = user;
        }

        if (user is { IsActive: true })
        {
            emails.Enqueue(user, [notification]);
        }
    }
}
