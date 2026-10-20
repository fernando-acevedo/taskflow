using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Projects;
using TaskFlow.Application.Tasks;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Dashboard;

public sealed record StatusCount(TaskItemStatus Status, int Count);

public sealed record PriorityCount(TaskPriority Priority, int Count);

/// <param name="UserId">Null for unassigned tasks.</param>
/// <param name="DisplayName">Null when unassigned, or when the user no longer exists.</param>
public sealed record AssigneeLoad(Guid? UserId, string? DisplayName, int OpenTasks, int OverdueTasks);

/// <summary>
/// A project's metrics. Open means not Done. <see cref="ByStatus"/> and <see cref="OpenByPriority"/> list every
/// status and priority, zeros included, in board and severity order.
/// </summary>
public sealed record ProjectDashboardDto(
    Guid ProjectId,
    int TotalTasks,
    int OpenTasks,
    int OverdueTasks,
    int DueThisWeek,
    IReadOnlyList<StatusCount> ByStatus,
    IReadOnlyList<PriorityCount> OpenByPriority,
    IReadOnlyList<AssigneeLoad> OpenByAssignee);

/// <summary>Computed with grouped queries in the database; no task rows are loaded.</summary>
public sealed class GetProjectDashboardHandler(IAppDbContext db, ProjectAccess access, IIdentityService identity, TimeProvider time)
{
    public async Task<Result<ProjectDashboardDto>> HandleAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var allowed = await access.GetAsync(projectId, ProjectAccessLevel.View, cancellationToken);
        if (!allowed.IsSuccess)
        {
            return allowed.Error;
        }

        var today = time.Today();
        var weekEnd = today.AddDays(6);
        var tasks = db.Tasks.Where(task => task.ProjectId == projectId);
        var open = tasks.Where(task => task.Status != TaskItemStatus.Done);

        var byStatus = await tasks
            .GroupBy(task => task.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Status, row => row.Count, cancellationToken);

        var byPriority = await open
            .GroupBy(task => task.Priority)
            .Select(group => new { Priority = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Priority, row => row.Count, cancellationToken);

        var byAssignee = await open
            .GroupBy(task => task.AssigneeId)
            .Select(group => new
            {
                UserId = group.Key,
                Open = group.Count(),
                Overdue = group.Count(task => task.DueDate < today),
            })
            .ToListAsync(cancellationToken);

        var dueThisWeek = await open.CountAsync(task => task.DueDate >= today && task.DueDate <= weekEnd, cancellationToken);

        var userIds = byAssignee.Where(row => row.UserId is not null).Select(row => row.UserId!.Value).ToList();
        var names = (await identity.FindByIdsAsync(userIds, cancellationToken)).ToDictionary(user => user.Id, user => user.DisplayName);

        return new ProjectDashboardDto(
            projectId,
            TotalTasks: byStatus.Values.Sum(),
            OpenTasks: byAssignee.Sum(row => row.Open),
            OverdueTasks: byAssignee.Sum(row => row.Overdue),
            DueThisWeek: dueThisWeek,
            ByStatus: [.. Enum.GetValues<TaskItemStatus>().Select(status => new StatusCount(status, byStatus.GetValueOrDefault(status)))],
            OpenByPriority: [.. Enum.GetValues<TaskPriority>().Select(priority => new PriorityCount(priority, byPriority.GetValueOrDefault(priority)))],
            OpenByAssignee:
            [
                .. byAssignee
                    .Select(row => new AssigneeLoad(
                        row.UserId,
                        row.UserId is { } id ? names.GetValueOrDefault(id) : null,
                        row.Open,
                        row.Overdue))
                    .OrderByDescending(load => load.OpenTasks)
                    .ThenBy(load => load.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            ]);
    }
}
