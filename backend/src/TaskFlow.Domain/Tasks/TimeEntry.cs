using TaskFlow.Domain.Tenancy;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Domain.Tasks;

/// <summary>
/// Time a user logged on a task for one day. Changes take the owning <see cref="Project"/> so that an archived
/// project stays read-only; the owner of the entry, or a project manager, edits or deletes it.
/// </summary>
public sealed class TimeEntry : ITenantOwned
{
    public const int MinMinutes = 1;
    public const int MaxMinutes = 1440;
    public const int NoteMaxLength = 500;

    private TimeEntry(Guid id, Guid taskItemId, Guid userId, DateOnly date, int minutes, string? note, DateTimeOffset createdAt)
    {
        Id = id;
        TaskItemId = taskItemId;
        UserId = userId;
        Date = date;
        Minutes = minutes;
        Note = note;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid TaskItemId { get; private set; }

    public Guid UserId { get; private set; }

    public DateOnly Date { get; private set; }

    public int Minutes { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <param name="today">The current date (UTC); later dates are refused.</param>
    public static Result<TimeEntry> Create(
        Project project,
        Guid taskItemId,
        Guid userId,
        DateOnly date,
        int minutes,
        string? note,
        DateOnly today,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(project);
        var check = Check(project, date, minutes, note, today);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        return new TimeEntry(Guid.CreateVersion7(now), taskItemId, userId, date, minutes, NormalizeNote(note), now);
    }

    public Result Edit(Project project, Guid editorId, bool canManageProject, DateOnly date, int minutes, string? note, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(project);
        var allowed = CanChange(project, editorId, canManageProject);
        if (!allowed.IsSuccess)
        {
            return allowed;
        }

        var check = Check(project, date, minutes, note, today);
        if (!check.IsSuccess)
        {
            return check;
        }

        Date = date;
        Minutes = minutes;
        Note = NormalizeNote(note);
        return Result.Success();
    }

    /// <summary>The owner of the entry or a project manager (owner) may edit or delete it, only while the project is editable.</summary>
    public Result CanChange(Project project, Guid userId, bool canManageProject)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.IsArchived)
        {
            return ProjectErrors.Archived;
        }

        return userId == UserId || canManageProject ? Result.Success() : TimeEntryErrors.NotOwner;
    }

    private static Result Check(Project project, DateOnly date, int minutes, string? note, DateOnly today)
    {
        if (project.IsArchived)
        {
            return ProjectErrors.Archived;
        }

        if (minutes is < MinMinutes or > MaxMinutes)
        {
            return TimeEntryErrors.InvalidMinutes;
        }

        if (note?.Trim().Length > NoteMaxLength)
        {
            return TimeEntryErrors.NoteTooLong;
        }

        // One day of margin covers time zones ahead of UTC, whose local day has already changed.
        return date > today.AddDays(1) ? TimeEntryErrors.FutureDate : Result.Success();
    }

    private static string? NormalizeNote(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim();
}

public static class TimeEntryErrors
{
    public static readonly Error NotFound =
        Error.NotFound("TimeEntries.NotFound", "The time entry was not found.");

    public static readonly Error NotOwner =
        Error.Forbidden("TimeEntries.NotOwner", "Only the person who logged the time or a project owner can change it.");

    public static readonly Error FutureDate =
        Error.Invalid("TimeEntries.FutureDate", "Time can't be logged on a future date.");

    public static readonly Error InvalidRange =
        Error.Invalid("TimeEntries.InvalidRange", "Pick a start and an end date, with the start no later than the end, in a range of up to 366 days.");

    public static readonly Error InvalidMinutes =
        Error.Invalid("TimeEntries.InvalidMinutes", "Minutes must be between 1 and 1440.");

    public static readonly Error NoteTooLong =
        Error.Invalid("TimeEntries.NoteTooLong", "The note is too long.");
}
