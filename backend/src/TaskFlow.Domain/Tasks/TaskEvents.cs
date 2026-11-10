using TaskFlow.Domain.Common;

namespace TaskFlow.Domain.Tasks;

public sealed record TaskCreated(
    Guid ProjectId,
    string ProjectName,
    Guid TaskId,
    string Title,
    TaskItemStatus Status,
    TaskPriority Priority,
    Guid? AssigneeId) : IDomainEvent;

/// <summary>What changed in a task's fields and labels; a null member did not change.</summary>
public sealed record TaskFieldChanges(
    Change<string>? Title,
    Change<TaskPriority>? Priority,
    Change<DateOnly?>? DueDate,
    Change<int?>? EstimatedMinutes,
    Change<DateOnly?>? StartDate,
    Change<bool>? IsBlocked,
    bool StatusNoteChanged,
    bool DescriptionChanged,
    IReadOnlyList<string> LabelsAdded,
    IReadOnlyList<string> LabelsRemoved)
{
    public bool Any =>
        Title is not null || Priority is not null || DueDate is not null || EstimatedMinutes is not null || StartDate is not null
        || IsBlocked is not null || StatusNoteChanged || DescriptionChanged || LabelsAdded.Count > 0 || LabelsRemoved.Count > 0;
}

/// <summary>Raised once per unit of work for the net change of a task (not for its creation or its moves).</summary>
public sealed record TaskUpdated(
    Guid ProjectId,
    string ProjectName,
    Guid TaskId,
    string Title,
    TaskFieldChanges Fields,
    Guid? PreviousAssigneeId,
    Guid? AssigneeId) : IDomainEvent
{
    public bool AssigneeChanged => PreviousAssigneeId != AssigneeId;
}

/// <summary>A task was put at a position of a column; <see cref="From"/> equals <see cref="To"/> when it stayed in its column.</summary>
public sealed record TaskMoved(
    Guid ProjectId,
    string ProjectName,
    Guid TaskId,
    string Title,
    TaskItemStatus From,
    TaskItemStatus To) : IDomainEvent;

/// <summary>Other tasks changed position because of a move. The tasks are read after the save, so they carry their new row version.</summary>
public sealed record TasksReordered(Guid ProjectId, IReadOnlyList<TaskItem> Tasks) : IDomainEvent;

public sealed record TaskDeleted(Guid ProjectId, Guid TaskId, string Title) : IDomainEvent;

public sealed record CommentAdded(
    Guid ProjectId,
    string ProjectName,
    Guid TaskId,
    Guid CommentId,
    IReadOnlyList<Guid> MentionedUserIds) : IDomainEvent;

/// <summary>A comment was edited; <see cref="NewMentionedUserIds"/> are the people this edit mentions for the first time.</summary>
public sealed record CommentUpdated(
    Guid ProjectId,
    string ProjectName,
    Guid TaskId,
    Guid CommentId,
    IReadOnlyList<Guid> NewMentionedUserIds) : IDomainEvent;

public sealed record CommentDeleted(Guid ProjectId, Guid TaskId, Guid CommentId) : IDomainEvent;
