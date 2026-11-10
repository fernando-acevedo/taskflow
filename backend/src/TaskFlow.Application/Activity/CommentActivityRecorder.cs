using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Activity;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Activity;

/// <summary>Writes the activity entries of comment events before the commit (ADR 0009, ADR 0024).</summary>
public sealed class CommentActivityRecorder(IAppDbContext db, ActivityLog activity) :
    IDomainEventHandler<CommentAdded>,
    IDomainEventHandler<CommentDeleted>
{
    public async Task HandleAsync(CommentAdded domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var taskTitle = await TitleAsync(domainEvent.TaskId, cancellationToken);
        activity.Record(domainEvent.ProjectId, ActivityType.CommentAdded, new { taskTitle, commentId = domainEvent.CommentId }, domainEvent.TaskId);
    }

    public async Task HandleAsync(CommentDeleted domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var taskTitle = await TitleAsync(domainEvent.TaskId, cancellationToken);
        activity.Record(domainEvent.ProjectId, ActivityType.CommentDeleted, new { taskTitle, commentId = domainEvent.CommentId }, domainEvent.TaskId);
    }

    private Task<string?> TitleAsync(Guid taskId, CancellationToken cancellationToken) =>
        db.Tasks.AsNoTracking().Where(task => task.Id == taskId).Select(task => task.Title).FirstOrDefaultAsync(cancellationToken);
}
