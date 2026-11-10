using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Notifications;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Notifications;

/// <summary>
/// Creates the notifications (and the outbox rows of their emails) for task and comment events before the commit, so they are saved
/// with the change (ADR 0009, ADR 0010, ADR 0018, ADR 0024).
/// </summary>
public sealed class TaskNotificationRecorder(IAppDbContext db, Notifier notifier) :
    IDomainEventHandler<TaskCreated>,
    IDomainEventHandler<TaskUpdated>,
    IDomainEventHandler<CommentAdded>,
    IDomainEventHandler<CommentUpdated>
{
    public async Task HandleAsync(TaskCreated domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        if (domainEvent.AssigneeId is { } assigneeId)
        {
            await notifier.NotifyAsync(
                NotificationType.TaskAssigned, assigneeId, domainEvent.ProjectId, domainEvent.ProjectName, domainEvent.TaskId, domainEvent.Title, null, cancellationToken);
        }
    }

    public async Task HandleAsync(TaskUpdated domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        if (domainEvent.AssigneeChanged && domainEvent.AssigneeId is { } assigneeId)
        {
            await notifier.NotifyAsync(
                NotificationType.TaskAssigned, assigneeId, domainEvent.ProjectId, domainEvent.ProjectName, domainEvent.TaskId, domainEvent.Title, null, cancellationToken);
        }
    }

    public async Task HandleAsync(CommentAdded domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var task = await FindTaskAsync(domainEvent.TaskId, cancellationToken);
        foreach (var userId in domainEvent.MentionedUserIds)
        {
            await notifier.NotifyAsync(
                NotificationType.Mentioned, userId, domainEvent.ProjectId, domainEvent.ProjectName, domainEvent.TaskId, task.Title, domainEvent.CommentId, cancellationToken);
        }

        // A mention already tells the assignee about this comment, so they get one notification, not two.
        if (task.AssigneeId is { } assigneeId && !domainEvent.MentionedUserIds.Contains(assigneeId))
        {
            await notifier.NotifyAsync(
                NotificationType.CommentOnYourTask, assigneeId, domainEvent.ProjectId, domainEvent.ProjectName, domainEvent.TaskId, task.Title, domainEvent.CommentId, cancellationToken);
        }
    }

    /// <summary>Only the people an edit adds are notified; the ones already mentioned were notified before.</summary>
    public async Task HandleAsync(CommentUpdated domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        if (domainEvent.NewMentionedUserIds.Count == 0)
        {
            return;
        }

        var task = await FindTaskAsync(domainEvent.TaskId, cancellationToken);
        foreach (var userId in domainEvent.NewMentionedUserIds)
        {
            await notifier.NotifyAsync(
                NotificationType.Mentioned, userId, domainEvent.ProjectId, domainEvent.ProjectName, domainEvent.TaskId, task.Title, domainEvent.CommentId, cancellationToken);
        }
    }

    private async Task<(string Title, Guid? AssigneeId)> FindTaskAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var found = await db.Tasks.AsNoTracking()
            .Where(task => task.Id == taskId)
            .Select(task => new { task.Title, task.AssigneeId })
            .FirstOrDefaultAsync(cancellationToken);
        return (found?.Title ?? string.Empty, found?.AssigneeId);
    }
}
