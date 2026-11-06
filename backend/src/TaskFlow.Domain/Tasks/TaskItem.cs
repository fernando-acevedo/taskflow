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
public sealed class TaskItem
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 4000;

    private readonly List<Label> _labels = [];

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
}

public static class TaskErrors
{
    public static readonly Error NotFound =
        Error.NotFound("Tasks.NotFound", "The task was not found.");
}
