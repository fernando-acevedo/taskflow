using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.TimeTracking;

public sealed record TimeEntryDto(
    Guid Id,
    Guid TaskId,
    Guid UserId,
    string UserName,
    DateOnly Date,
    int Minutes,
    string? Note,
    DateTimeOffset CreatedAt)
{
    public static TimeEntryDto From(TimeEntry entry, string userName)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new TimeEntryDto(entry.Id, entry.TaskItemId, entry.UserId, userName, entry.Date, entry.Minutes, entry.Note, entry.CreatedAt);
    }
}

/// <summary>A task's time entries (newest day first), the total logged and the task's estimate.</summary>
public sealed record TaskTimeDto(
    IReadOnlyList<TimeEntryDto> Entries,
    int TotalMinutes,
    int? EstimatedMinutes);
