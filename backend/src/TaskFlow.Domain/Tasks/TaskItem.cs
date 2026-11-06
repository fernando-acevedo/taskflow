using System.Text.RegularExpressions;
using TaskFlow.Domain.Tenancy;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Domain.Tasks;

public enum TaskItemStatus
{
    ToDo,
    InProgress,
    Review,
    Done,
}

public enum TaskPriority
{
    Low,
    Medium,
    High,
    Critical,
}

/// <summary>
/// A task on a project's board. Named TaskItem to avoid clashing with <see cref="Task"/>. Changes take the
/// owning <see cref="Project"/> so its rules apply: no changes while archived, and the assignee is a member.
/// </summary>
public sealed partial class TaskItem : ITenantOwned
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 4000;
    public const int ExternalKeyMaxLength = 100;
    public const int StatusNoteMaxLength = 500;
    public const int MaxDependencies = 50;

    private readonly List<Label> _labels = [];
    private readonly List<TaskDependency> _dependencies = [];

    private TaskItem(Guid id, Guid projectId, string title, Guid createdById, DateTimeOffset createdAt)
    {
        Id = id;
        ProjectId = projectId;
        Title = title;
        CreatedById = createdById;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    public string Title { get; private set; }

    public string? Description { get; private set; }

    public TaskItemStatus Status { get; private set; }

    public TaskPriority Priority { get; private set; }

    public Guid? AssigneeId { get; private set; }

    public DateOnly? DueDate { get; private set; }

    /// <summary>Estimated effort in minutes; null when nobody estimated it.</summary>
    public int? EstimatedMinutes { get; private set; }

    /// <summary>Order within the status column, starting at 0.</summary>
    public int Position { get; private set; }

    public Guid CreatedById { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Set while the task is Done.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Optimistic concurrency token, maintained by the database.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<Label> Labels => _labels;

    /// <summary>
    /// An id chosen by whoever syncs the task from outside (the Gantt, a script), unique within the project
    /// (case-insensitive); it makes repeating a push safe (ADR 0017). Once set it does not change.
    /// </summary>
    public string? ExternalKey { get; private set; }

    /// <summary>When the work is planned to begin; never after <see cref="DueDate"/>.</summary>
    public DateOnly? StartDate { get; private set; }

    /// <summary>The task is stuck. A flag, not a column of the board (ADR 0017).</summary>
    public bool IsBlocked { get; private set; }

    /// <summary>The latest note about the state of the work, e.g. why it is blocked.</summary>
    public string? StatusNote { get; private set; }

    /// <summary>The tasks this one depends on.</summary>
    public IReadOnlyCollection<TaskDependency> Dependencies => _dependencies;

    /// <summary>Creates a To Do task at the given position of that column.</summary>
    public static Result<TaskItem> Create(
        Project project,
        string title,
        string? description,
        TaskPriority priority,
        Guid? assigneeId,
        DateOnly? dueDate,
        Guid createdById,
        int position,
        DateTimeOffset now,
        int? estimatedMinutes = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        var check = project.CanChangeTask(assigneeId);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        return new TaskItem(Guid.CreateVersion7(now), project.Id, title, createdById, now)
        {
            Description = description,
            Priority = priority,
            AssigneeId = assigneeId,
            DueDate = dueDate,
            Position = position,
            EstimatedMinutes = estimatedMinutes,
        };
    }

    public Result Update(
        Project project,
        string title,
        string? description,
        TaskPriority priority,
        Guid? assigneeId,
        DateOnly? dueDate,
        DateTimeOffset now,
        int? estimatedMinutes = null)
    {
        var check = CheckProject(project, assigneeId);
        if (!check.IsSuccess)
        {
            return check;
        }

        if (StartDate > dueDate)
        {
            return TaskErrors.StartAfterDue;
        }

        Title = title;
        Description = description;
        Priority = priority;
        AssigneeId = assigneeId;
        DueDate = dueDate;
        EstimatedMinutes = estimatedMinutes;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Clears the assignee because the person left the tenant. Unlike <see cref="Update"/> this also works in an
    /// archived project: it is an administrative cleanup, not an edit by a project member.
    /// </summary>
    public void Unassign(DateTimeOffset now)
    {
        AssigneeId = null;
        UpdatedAt = now;
    }

    /// <summary>
    /// Moves the task to a column. Entering Done sets <see cref="CompletedAt"/>; leaving it clears it.
    /// The caller renumbers the other tasks in the affected columns with <see cref="Reposition"/>.
    /// </summary>
    public Result Move(Project project, TaskItemStatus status, int position, DateTimeOffset now)
    {
        var check = CheckProject(project, AssigneeId);
        if (!check.IsSuccess)
        {
            return check;
        }

        if (status != Status)
        {
            CompletedAt = status == TaskItemStatus.Done ? now : null;
            Status = status;
            UpdatedAt = now;
        }

        Position = position;
        return Result.Success();
    }

    /// <summary>Replaces the task's labels; all of them must belong to the task's project.</summary>
    public Result SetLabels(Project project, IReadOnlyCollection<Label> labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        var check = CheckProject(project, AssigneeId);
        if (!check.IsSuccess)
        {
            return check;
        }

        if (labels.Any(label => label.ProjectId != ProjectId))
        {
            return LabelErrors.NotInProject;
        }

        var wanted = labels.Select(label => label.Id).ToHashSet();
        _labels.RemoveAll(label => !wanted.Contains(label.Id));
        foreach (var label in labels)
        {
            if (_labels.TrueForAll(existing => existing.Id != label.Id))
            {
                _labels.Add(label);
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// Sets the planning fields. A blank <paramref name="externalKey"/> or <paramref name="statusNote"/> means none. The key
    /// may be given once; sending a different one afterwards is an error, the same one is fine.
    /// </summary>
    public Result SetPlanning(Project project, string? externalKey, DateOnly? startDate, bool isBlocked, string? statusNote, DateTimeOffset now)
    {
        var check = CheckProject(project, AssigneeId);
        if (!check.IsSuccess)
        {
            return check;
        }

        var key = string.IsNullOrWhiteSpace(externalKey) ? null : externalKey.Trim();
        if (key is not null && (key.Length > ExternalKeyMaxLength || !ExternalKeyPattern().IsMatch(key)))
        {
            return TaskErrors.ExternalKeyInvalid;
        }

        if (ExternalKey is not null && key is not null && !string.Equals(ExternalKey, key, StringComparison.OrdinalIgnoreCase))
        {
            return TaskErrors.ExternalKeyLocked;
        }

        if (startDate > DueDate)
        {
            return TaskErrors.StartAfterDue;
        }

        ExternalKey ??= key;
        StartDate = startDate;
        IsBlocked = isBlocked;
        StatusNote = string.IsNullOrWhiteSpace(statusNote) ? null : statusNote.Trim();
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Replaces the tasks this one depends on. They must be other tasks of the same project and must not close a loop
    /// with <paramref name="otherEdges"/>, the project's dependencies as (task, depends on) pairs, which may include the
    /// ones of this task (they are being replaced and are ignored).
    /// </summary>
    public Result SetDependencies(
        Project project,
        IReadOnlyCollection<TaskItem> dependsOn,
        IEnumerable<(Guid TaskId, Guid DependsOnId)> otherEdges,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(dependsOn);
        ArgumentNullException.ThrowIfNull(otherEdges);
        var check = CheckProject(project, AssigneeId);
        if (!check.IsSuccess)
        {
            return check;
        }

        var wanted = dependsOn.DistinctBy(task => task.Id).ToList();
        if (wanted.Count > MaxDependencies)
        {
            return TaskErrors.TooManyDependencies;
        }

        if (wanted.Any(task => task.Id == Id))
        {
            return TaskErrors.DependencyOnSelf;
        }

        if (wanted.Any(task => task.ProjectId != ProjectId))
        {
            return TaskErrors.DependencyNotInProject;
        }

        var ids = wanted.Select(task => task.Id).ToHashSet();
        if (TaskDependency.CreatesCycle(Id, ids, otherEdges.Where(edge => edge.TaskId != Id)))
        {
            return TaskErrors.DependencyCycle;
        }

        var removed = _dependencies.RemoveAll(dependency => !ids.Contains(dependency.DependsOnTaskId));
        var added = 0;
        foreach (var id in ids)
        {
            if (_dependencies.TrueForAll(existing => existing.DependsOnTaskId != id))
            {
                _dependencies.Add(new TaskDependency(Id, id));
                added++;
            }
        }

        if (removed + added > 0)
        {
            UpdatedAt = now;
        }

        return Result.Success();
    }

    /// <summary>Changes only the order within the current column, after a neighbor moved.</summary>
    public void Reposition(int position) => Position = position;

    private Result CheckProject(Project project, Guid? assigneeId)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.Id != ProjectId)
        {
            throw new ArgumentException("The task belongs to another project.", nameof(project));
        }

        // Only a new assignee must be a member: someone who left the project can stay on their tasks.
        return project.CanChangeTask(assigneeId == AssigneeId ? null : assigneeId);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]*$")]
    private static partial Regex ExternalKeyPattern();
}

public static class TaskErrors
{
    public static readonly Error NotFound =
        Error.NotFound("Tasks.NotFound", "The task was not found.");

    public static readonly Error ExternalKeyInvalid =
        Error.Invalid("Tasks.ExternalKeyInvalid", "The external key may only contain letters, digits, dots, colons, underscores and hyphens, and is at most 100 characters.");

    public static readonly Error ExternalKeyLocked =
        Error.Conflict("Tasks.ExternalKeyLocked", "The task already has another external key, which can't be changed.");

    public static readonly Error ExternalKeyTaken =
        Error.Conflict("Tasks.ExternalKeyTaken", "Another task of the project already uses this external key.");

    public static readonly Error StartAfterDue =
        Error.Invalid("Tasks.StartAfterDue", "The start date can't be after the due date.");

    public static readonly Error DependencyOnSelf =
        Error.Invalid("Tasks.DependencyOnSelf", "A task can't depend on itself.");

    public static readonly Error DependencyNotInProject =
        Error.Invalid("Tasks.DependencyNotInProject", "A task can only depend on tasks of the same project.");

    public static readonly Error DependencyCycle =
        Error.Invalid("Tasks.DependencyCycle", "These dependencies would make tasks wait for each other in a loop.");

    public static readonly Error TooManyDependencies =
        Error.Invalid("Tasks.TooManyDependencies", "A task can depend on at most 50 tasks.");
}
