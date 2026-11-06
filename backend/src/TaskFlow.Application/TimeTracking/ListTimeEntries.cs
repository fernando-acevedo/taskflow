using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Projects;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.TimeTracking;

public sealed record ListTimeEntriesQuery(Guid ProjectId, Guid TaskId);

/// <summary>
/// Everyone who sees the project reads their own entries on a task; owners read everyone's. The total and the estimate
/// are the task's (all entries), to compare estimated and actual time.
/// </summary>
public sealed class ListTimeEntriesHandler(IAppDbContext db, ProjectAccess access, IIdentityService identity, ICurrentUser currentUser)
{
    public async Task<Result<TaskTimeDto>> HandleAsync(ListTimeEntriesQuery query, CancellationToken cancellationToken)
    {
        var allowed = await access.GetAsync(query.ProjectId, ProjectAccessLevel.View, cancellationToken);
        if (!allowed.IsSuccess)
        {
            return allowed.Error;
        }

        var estimate = await db.Tasks.AsNoTracking()
            .Where(task => task.Id == query.TaskId && task.ProjectId == query.ProjectId)
            .Select(task => new { task.EstimatedMinutes })
            .FirstOrDefaultAsync(cancellationToken);
        if (estimate is null)
        {
            return TaskErrors.NotFound;
        }

        var taskEntries = db.TimeEntries.AsNoTracking().Where(entry => entry.TaskItemId == query.TaskId);
        var total = await taskEntries.SumAsync(entry => entry.Minutes, cancellationToken);

        var canManage = ProjectAccess.Allows(allowed.Value, ProjectAccessLevel.Manage, currentUser);
        var userId = currentUser.Id;
        var entries = await taskEntries
            .Where(entry => canManage || entry.UserId == userId)
            .OrderByDescending(entry => entry.Date)
            .ThenByDescending(entry => entry.CreatedAt)
            .ThenBy(entry => entry.Id)
            .ToListAsync(cancellationToken);

        var names = (await identity.FindByIdsAsync([.. entries.Select(entry => entry.UserId).Distinct()], cancellationToken))
            .ToDictionary(user => user.Id, user => user.DisplayName);

        return new TaskTimeDto(
            [.. entries.Select(entry => TimeEntryDto.From(entry, names.GetValueOrDefault(entry.UserId, string.Empty)))],
            total,
            estimate.EstimatedMinutes);
    }
}
