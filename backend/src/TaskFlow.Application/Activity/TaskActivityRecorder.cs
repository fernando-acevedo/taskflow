using System.Text.Json.Serialization;
using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Activity;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Activity;

/// <summary>
/// Writes the activity entries of task events (ADR 0009, ADR 0024). Before the commit, so the entries are saved with the change.
/// </summary>
public sealed class TaskActivityRecorder(ActivityLog activity) :
    IDomainEventHandler<TaskCreated>,
    IDomainEventHandler<TaskUpdated>,
    IDomainEventHandler<TaskMoved>,
    IDomainEventHandler<TaskDeleted>
{
    public Task HandleAsync(TaskCreated domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        activity.Record(
            domainEvent.ProjectId,
            ActivityType.TaskCreated,
            new { taskTitle = domainEvent.Title, status = domainEvent.Status, priority = domainEvent.Priority, assigneeId = domainEvent.AssigneeId },
            domainEvent.TaskId);
        return Task.CompletedTask;
    }

    /// <summary>One TaskUpdated entry for the fields and labels that changed, and a TaskAssigned one if the assignee did.</summary>
    public Task HandleAsync(TaskUpdated domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var fields = domainEvent.Fields;
        if (fields.Any)
        {
            activity.Record(
                domainEvent.ProjectId,
                ActivityType.TaskUpdated,
                new
                {
                    taskTitle = domainEvent.Title,
                    title = Both(fields.Title),
                    priority = Both(fields.Priority),
                    dueDate = Both(fields.DueDate),
                    estimatedMinutes = Both(fields.EstimatedMinutes),
                    startDate = Both(fields.StartDate),
                    isBlocked = Both(fields.IsBlocked),
                    statusNoteChanged = fields.StatusNoteChanged ? true : (bool?)null,
                    descriptionChanged = fields.DescriptionChanged ? true : (bool?)null,
                    labelsAdded = fields.LabelsAdded.Count > 0 ? fields.LabelsAdded : null,
                    labelsRemoved = fields.LabelsRemoved.Count > 0 ? fields.LabelsRemoved : null,
                },
                domainEvent.TaskId);
        }

        if (domainEvent.AssigneeChanged)
        {
            activity.Record(
                domainEvent.ProjectId,
                ActivityType.TaskAssigned,
                new { taskTitle = domainEvent.Title, assigneeId = domainEvent.AssigneeId, previousAssigneeId = domainEvent.PreviousAssigneeId },
                domainEvent.TaskId);
        }

        return Task.CompletedTask;
    }

    public Task HandleAsync(TaskMoved domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        if (domainEvent.From != domainEvent.To)
        {
            activity.Record(
                domainEvent.ProjectId,
                ActivityType.TaskMoved,
                new { taskTitle = domainEvent.Title, from = domainEvent.From, to = domainEvent.To },
                domainEvent.TaskId);
        }

        return Task.CompletedTask;
    }

    public Task HandleAsync(TaskDeleted domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        activity.Record(domainEvent.ProjectId, ActivityType.TaskDeleted, new { taskTitle = domainEvent.Title }, domainEvent.TaskId);
        return Task.CompletedTask;
    }

    private static ValueChange<T>? Both<T>(Change<T>? change) => change is null ? null : new ValueChange<T>(change.From, change.To);

    // Always writes both keys, so that setting or clearing a value shows up as a null instead of a missing key.
    private sealed record ValueChange<T>(
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] T From,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] T To);
}
