using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Projects;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.TimeTracking;

/// <param name="ProjectId">The project.</param>
/// <param name="From">First day, inclusive; null for no lower bound.</param>
/// <param name="To">Last day, inclusive; null for no upper bound.</param>
/// <param name="UserId">Only this person's time; null for everyone.</param>
public sealed record GetTimeSummaryQuery(Guid ProjectId, DateOnly? From, DateOnly? To, Guid? UserId);

public sealed record TimeByUserDto(Guid UserId, string UserName, int Minutes);

/// <param name="TaskId">The task.</param>
/// <param name="Title">The task's title.</param>
/// <param name="EstimatedMinutes">The task's estimate (whole, not limited to the range).</param>
/// <param name="ActualMinutes">The time logged on the task within the range.</param>
public sealed record TimeByTaskDto(Guid TaskId, string Title, int? EstimatedMinutes, int ActualMinutes);

public sealed record TimeSummaryDto(
    int TotalMinutes,
    IReadOnlyList<TimeByUserDto> ByUser,
    IReadOnlyList<TimeByTaskDto> ByTask);

/// <summary>The project's logged time in a date range: total, per person and per task. Only owners read it.</summary>
public sealed class GetTimeSummaryHandler(IAppDbContext db, ProjectAccess access, IIdentityService identity)
{
    public const int MaxRangeDays = 366;

    public async Task<Result<TimeSummaryDto>> HandleAsync(GetTimeSummaryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var allowed = await access.GetAsync(query.ProjectId, ProjectAccessLevel.Manage, cancellationToken);
        if (!allowed.IsSuccess)
        {
            return allowed.Error;
        }

        var range = ValidateRange(query.From, query.To);
        if (!range.IsSuccess)
        {
            return range.Error;
        }

        var entries = TimeSummaryFilter.Entries(db, query);

        var byUserRows = await entries
            .GroupBy(entry => entry.UserId)
            .Select(group => new { UserId = group.Key, Minutes = group.Sum(entry => entry.Minutes) })
            .ToListAsync(cancellationToken);
        var byTaskRows = await entries
            .GroupBy(entry => entry.TaskItemId)
            .Select(group => new { TaskId = group.Key, Minutes = group.Sum(entry => entry.Minutes) })
            .ToListAsync(cancellationToken);
        var taskIds = byTaskRows.Select(row => row.TaskId).ToList();
        var tasks = await db.Tasks.AsNoTracking()
            .Where(task => taskIds.Contains(task.Id))
            .Select(task => new { task.Id, task.Title, task.EstimatedMinutes })
            .ToDictionaryAsync(task => task.Id, cancellationToken);
        var byTask = byTaskRows
            .Select(row => tasks.TryGetValue(row.TaskId, out var task)
                ? new TimeByTaskDto(row.TaskId, task.Title, task.EstimatedMinutes, row.Minutes)
                : new TimeByTaskDto(row.TaskId, string.Empty, null, row.Minutes))
            .ToList();

        var names = (await identity.FindByIdsAsync([.. byUserRows.Select(row => row.UserId)], cancellationToken))
            .ToDictionary(user => user.Id, user => user.DisplayName);
        var byUser = byUserRows
            .Select(row => new TimeByUserDto(row.UserId, names.GetValueOrDefault(row.UserId, string.Empty), row.Minutes))
            .OrderByDescending(row => row.Minutes)
            .ThenBy(row => row.UserName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new TimeSummaryDto(
            byUser.Sum(row => row.Minutes),
            byUser,
            [.. byTask.OrderByDescending(row => row.ActualMinutes).ThenBy(row => row.Title, StringComparer.CurrentCultureIgnoreCase)]);
    }

    /// <summary>A range (with both ends when <paramref name="required"/>) must start no later than it ends and span at most <see cref="MaxRangeDays"/> days.</summary>
    internal static Result ValidateRange(DateOnly? from, DateOnly? to, bool required = false)
    {
        if (from is not { } start || to is not { } end)
        {
            return required ? TimeEntryErrors.InvalidRange : Result.Success();
        }

        return start > end || end.DayNumber - start.DayNumber + 1 > MaxRangeDays
            ? TimeEntryErrors.InvalidRange
            : Result.Success();
    }
}

internal static class TimeSummaryFilter
{
    /// <summary>The project's time entries within the query's range and user.</summary>
    public static IQueryable<TimeEntry> Entries(IAppDbContext db, GetTimeSummaryQuery query) =>
        db.TimeEntries.AsNoTracking().Where(entry =>
            db.Tasks.Any(task => task.Id == entry.TaskItemId && task.ProjectId == query.ProjectId)
            && (query.From == null || entry.Date >= query.From)
            && (query.To == null || entry.Date <= query.To)
            && (query.UserId == null || entry.UserId == query.UserId));
}
