using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Tasks;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Notifications;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Notifications;

/// <summary>
/// Raises a TaskDueSoon notification for the assignee of every open task, in a project that is not archived, that is
/// due today or tomorrow (UTC), and emails the users who chose the daily digest one message with the list. Running
/// it again the same day creates nothing new: a task and due date are notified once. A background service calls
/// it every day (ADR 0010); tests call it directly.
/// </summary>
public sealed class NotifyDueSoonHandler(
    IAppDbContext db,
    IIdentityService identity,
    ITenantContext tenant,
    INotificationEmailSender emails,
    TimeProvider time)
{
    /// <returns>How many notifications were created.</returns>
    public async Task<Result<int>> HandleAsync(CancellationToken cancellationToken)
    {
        if (tenant.TenantId is not { } tenantId)
        {
            return 0;
        }

        var today = time.Today();
        var tomorrow = today.AddDays(1);

        var due = await db.Tasks.AsNoTracking()
            .Where(task => task.AssigneeId != null
                && task.Status != TaskItemStatus.Done
                && task.DueDate != null && task.DueDate >= today && task.DueDate <= tomorrow
                && db.Projects.Any(project => project.Id == task.ProjectId && !project.IsArchived))
            .Select(task => new DueTask(
                task.Id,
                task.ProjectId,
                task.Title,
                task.AssigneeId!.Value,
                task.DueDate!.Value,
                db.Projects.Where(project => project.Id == task.ProjectId).Select(project => project.Name).First()))
            .ToListAsync(cancellationToken);
        if (due.Count == 0)
        {
            return 0;
        }

        var taskIds = due.Select(task => task.TaskId).ToList();
        var existing = (await db.Notifications.AsNoTracking()
                .Where(notification => notification.Type == NotificationType.TaskDueSoon
                    && notification.TaskItemId != null && taskIds.Contains(notification.TaskItemId.Value))
                .Select(notification => new { notification.TaskItemId, notification.UserId, notification.Data })
                .ToListAsync(cancellationToken))
            .Select(notification => (notification.TaskItemId, notification.UserId, DueDate: DueDateOf(notification.Data)))
            .ToHashSet();

        var recipientIds = due.Select(task => task.AssigneeId).Distinct().ToList();

        // Defense in depth: only people who still belong to this tenant are notified (a removed member may be left
        // as the assignee of data from before the removal, e.g. in an old backup or a manual change).
        var members = await db.TenantMemberships.AsNoTracking()
            .Where(membership => membership.TenantId == tenantId && recipientIds.Contains(membership.UserId))
            .Select(membership => membership.UserId)
            .ToListAsync(cancellationToken);
        var users = (await identity.FindByIdsAsync(members, cancellationToken))
            .Where(user => user.IsActive)
            .ToDictionary(user => user.Id);

        var now = time.GetUtcNow();
        var created = new List<Notification>();
        foreach (var task in due.Where(task => users.ContainsKey(task.AssigneeId)
            && !existing.Contains((task.TaskId, task.AssigneeId, task.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))))
        {
            var data = JsonSerializer.Serialize(
                new { taskTitle = task.Title, projectName = task.ProjectName, dueDate = task.DueDate }, Notifier.Json);
            created.Add(Notification.Create(
                task.AssigneeId, NotificationType.TaskDueSoon, task.ProjectId, task.TaskId, null, data, now));
        }

        if (created.Count == 0)
        {
            return 0;
        }

        db.Notifications.AddRange(created);
        await db.SaveChangesAsync(cancellationToken);

        await SendDigestsAsync(created, users, cancellationToken);
        return created.Count;
    }

    private async Task SendDigestsAsync(
        List<Notification> created, Dictionary<Guid, UserDto> users, CancellationToken cancellationToken)
    {
        var recipientIds = created.Select(notification => notification.UserId).Distinct().ToList();
        var subscribed = await db.NotificationPreferences.AsNoTracking()
            .Where(preferences => recipientIds.Contains(preferences.UserId) && preferences.EmailDailyDigest)
            .Select(preferences => preferences.UserId)
            .ToListAsync(cancellationToken);

        foreach (var userId in subscribed)
        {
            await emails.SendAsync(users[userId], [.. created.Where(notification => notification.UserId == userId)], cancellationToken);
        }
    }

    private static string? DueDateOf(string data)
    {
        using var document = JsonDocument.Parse(data);
        return document.RootElement.TryGetProperty("dueDate", out var value) ? value.GetString() : null;
    }

    private sealed record DueTask(Guid TaskId, Guid ProjectId, string Title, Guid AssigneeId, DateOnly DueDate, string ProjectName);
}
