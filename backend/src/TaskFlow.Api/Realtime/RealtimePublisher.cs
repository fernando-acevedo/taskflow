using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Tasks;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Api.Realtime;

/// <summary>
/// Publishes task and comment changes to the project's SignalR group once they are committed (ADR 0023, ADR 0024). Best effort: the
/// dispatcher logs a failure and carries on. It handles all the events of a save together, so a bulk sync reads the changed tasks with
/// one query per project instead of one per task. <c>originClientId</c> lets the tab that caused a change ignore its own echo.
/// </summary>
internal sealed partial class RealtimePublisher(
    IHubContext<ProjectsHub> hub,
    ITenantContext tenant,
    IClientOrigin origin,
    GetTasksByIdsHandler getTasks,
    ILogger<RealtimePublisher> logger) : IAfterCommitBatchHandler
{
    public async Task HandleAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (tenant.TenantId is null || !events.Any(IsRealtime))
        {
            return;
        }

        var deleted = events.OfType<TaskDeleted>().Select(e => e.TaskId).ToHashSet();
        var changed = await ReadChangedTasksAsync(events, deleted, cancellationToken);

        var sent = new HashSet<Guid>();
        foreach (var domainEvent in events)
        {
            switch (domainEvent)
            {
                case TaskCreated e:
                    await TaskChangedAsync(e.ProjectId, e.TaskId, changed, sent, cancellationToken);
                    break;
                case TaskUpdated e:
                    await TaskChangedAsync(e.ProjectId, e.TaskId, changed, sent, cancellationToken);
                    break;
                case TaskMoved e:
                    await TaskChangedAsync(e.ProjectId, e.TaskId, changed, sent, cancellationToken);
                    break;
                case TaskDeleted e:
                    await SendAsync(e.ProjectId, "taskDeleted", new { projectId = e.ProjectId, taskId = e.TaskId, originClientId = origin.ClientId }, cancellationToken);
                    break;
                case TasksReordered e:
                    var tasks = e.Tasks.Select(task => new TaskPositionDto(task.Id, task.Status, task.Position, task.RowVersion)).ToList();
                    await SendAsync(e.ProjectId, "tasksReordered", new { projectId = e.ProjectId, tasks, originClientId = origin.ClientId }, cancellationToken);
                    break;
                case CommentAdded e:
                    await CommentsChangedAsync(e.ProjectId, e.TaskId, cancellationToken);
                    break;
                case CommentUpdated e:
                    await CommentsChangedAsync(e.ProjectId, e.TaskId, cancellationToken);
                    break;
                case CommentDeleted e:
                    await CommentsChangedAsync(e.ProjectId, e.TaskId, cancellationToken);
                    break;
            }
        }
    }

    private static bool IsRealtime(IDomainEvent domainEvent) =>
        domainEvent is TaskCreated or TaskUpdated or TaskMoved or TaskDeleted or TasksReordered or CommentAdded or CommentUpdated or CommentDeleted;

    /// <summary>The tasks as the API serves them, read after the commit (the row version and the computed fields are the saved ones): one query per project.</summary>
    private async Task<Dictionary<Guid, TaskDto>> ReadChangedTasksAsync(IReadOnlyList<IDomainEvent> events, HashSet<Guid> deleted, CancellationToken cancellationToken)
    {
        var changed = new Dictionary<Guid, TaskDto>();
        var byProject = events
            .Select(e => e switch
            {
                TaskCreated c => (c.ProjectId, c.TaskId),
                TaskUpdated u => (u.ProjectId, u.TaskId),
                TaskMoved m => (m.ProjectId, m.TaskId),
                _ => default,
            })
            .Where(pair => pair.TaskId != Guid.Empty && !deleted.Contains(pair.TaskId))
            .GroupBy(pair => pair.ProjectId, pair => pair.TaskId);

        foreach (var project in byProject)
        {
            var ids = project.Distinct().ToList();
            var result = await getTasks.HandleAsync(new GetTasksByIdsQuery(project.Key, ids), cancellationToken);
            if (!result.IsSuccess)
            {
                LogReadFailed(project.Key, ids.Count, result.Error.Code);
                continue;
            }

            foreach (var task in result.Value)
            {
                changed[task.Id] = task;
            }

            if (result.Value.Count < ids.Count)
            {
                LogMissing(project.Key, ids.Count - result.Value.Count);
            }
        }

        return changed;
    }

    private async Task TaskChangedAsync(Guid projectId, Guid taskId, Dictionary<Guid, TaskDto> changed, HashSet<Guid> sent, CancellationToken cancellationToken)
    {
        // A task touched several times in the save is published once, with its final state.
        if (changed.TryGetValue(taskId, out var task) && sent.Add(taskId))
        {
            await SendAsync(projectId, "taskChanged", new { projectId, task, originClientId = origin.ClientId }, cancellationToken);
        }
    }

    private Task CommentsChangedAsync(Guid projectId, Guid taskId, CancellationToken cancellationToken) =>
        SendAsync(projectId, "commentsChanged", new { projectId, taskId, originClientId = origin.ClientId }, cancellationToken);

    private Task SendAsync(Guid projectId, string method, object payload, CancellationToken cancellationToken) =>
        tenant.TenantId is { } tenantId
            ? hub.Clients.Group(ProjectsHub.Group(tenantId, projectId)).SendAsync(method, payload, cancellationToken)
            : Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Real-time: reading {Count} changed task(s) of project {ProjectId} failed with {Code}; no update was published for them")]
    private partial void LogReadFailed(Guid projectId, int count, string code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Real-time: {Count} changed task(s) of project {ProjectId} were not found after the commit; no update was published for them")]
    private partial void LogMissing(Guid projectId, int count);
}
