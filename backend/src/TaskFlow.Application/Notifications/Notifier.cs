using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Notifications;

namespace TaskFlow.Application.Notifications;

/// <summary>
/// Handlers call <see cref="NotifyAsync"/> next to the change they make, and their own <c>SaveChangesAsync</c> stores
/// the notification in the same transaction (ADR 0009, ADR 0010). After saving they call <see cref="SendEmailsAsync"/>
/// for the users who asked for email. The current user is never notified about their own action.
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

    private readonly List<Notification> _pending = [];
    private readonly Dictionary<Guid, bool> _members = [];
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
        _pending.Add(notification);
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

    /// <summary>Emails the notifications added since the last call to the recipients whose preferences ask for it.</summary>
    public async Task SendEmailsAsync(CancellationToken cancellationToken)
    {
        if (_pending.Count == 0)
        {
            return;
        }

        var pending = _pending.ToList();
        _pending.Clear();
        var recipientIds = pending.Select(notification => notification.UserId).Distinct().ToList();
        var preferences = await db.NotificationPreferences.AsNoTracking()
            .Where(preference => recipientIds.Contains(preference.UserId))
            .ToListAsync(cancellationToken);
        var users = (await identity.FindByIdsAsync(recipientIds, cancellationToken)).ToDictionary(user => user.Id);

        foreach (var preference in preferences)
        {
            var wanted = pending
                .Where(notification => notification.UserId == preference.UserId && preference.WantsEmailFor(notification.Type))
                .ToList();
            if (wanted.Count > 0 && users.TryGetValue(preference.UserId, out var user) && user.IsActive)
            {
                await emails.SendAsync(user, wanted, cancellationToken);
            }
        }
    }
}
