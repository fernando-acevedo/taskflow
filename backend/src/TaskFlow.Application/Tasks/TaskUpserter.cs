using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Activity;
using TaskFlow.Application.Common;
using TaskFlow.Application.Projects;
using TaskFlow.Domain.Activity;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Tasks;

/// <summary>
/// What an upsert sets on a task (ADR 0017). Every field replaces the stored value, so leave out what should be empty;
/// the two exceptions are <paramref name="Status"/> (null keeps the status, or To Do for a new task) and
/// <paramref name="DependsOn"/> (null keeps the dependencies, an empty list clears them).
/// </summary>
/// <param name="DependsOn">External keys of the tasks this one depends on; they may be created by the same sync.</param>
/// <param name="Labels">Names of project labels: null keeps the task's labels, a list replaces them. Missing labels are created
/// when the caller may manage labels, otherwise the item fails with <c>Labels.CreateNotAllowed</c>.</param>
public sealed record TaskUpsertFields(
    string Title,
    string? Description,
    TaskPriority Priority = TaskPriority.Medium,
    Guid? AssigneeId = null,
    DateOnly? StartDate = null,
    DateOnly? DueDate = null,
    int? EstimatedMinutes = null,
    TaskItemStatus? Status = null,
    bool IsBlocked = false,
    string? StatusNote = null,
    IReadOnlyList<string>? DependsOn = null,
    IReadOnlyList<string>? Labels = null);

public sealed class TaskUpsertFieldsValidator : AbstractValidator<TaskUpsertFields>
{
    public TaskUpsertFieldsValidator()
    {
        RuleFor(fields => fields.Title).NotEmpty().MaximumLength(TaskItem.TitleMaxLength);
        RuleFor(fields => fields.Description).MaximumLength(TaskItem.DescriptionMaxLength);
        RuleFor(fields => fields.Priority).IsInEnum();
        RuleFor(fields => fields.Status).IsInEnum().When(fields => fields.Status.HasValue);
        RuleFor(fields => fields.EstimatedMinutes).InclusiveBetween(0, TaskQueries.MaxEstimatedMinutes);
        RuleFor(fields => fields.StatusNote).MaximumLength(TaskItem.StatusNoteMaxLength);
        RuleFor(fields => fields.DependsOn)
            .Must(keys => keys!.Count <= TaskItem.MaxDependencies)
            .When(fields => fields.DependsOn is not null);
        RuleForEach(fields => fields.DependsOn).NotEmpty().MaximumLength(TaskItem.ExternalKeyMaxLength);
        RuleFor(fields => fields.Labels)
            .Must(names => names!.Count <= TaskQueries.MaxLabelsPerTask)
            .When(fields => fields.Labels is not null);
        RuleForEach(fields => fields.Labels).NotEmpty().MaximumLength(Label.NameMaxLength);
    }
}

public static class TaskSyncErrors
{
    public static readonly Error DependencyKeyNotFound =
        Error.Invalid("Tasks.DependencyKeyNotFound", "A task this one depends on was not found in the project or in the same request.");

    public static readonly Error DuplicateExternalKey =
        Error.Invalid("Tasks.DuplicateExternalKey", "The same external key appears more than once in the request.");

    public static readonly Error LabelCreateNotAllowed =
        Error.Forbidden("Labels.CreateNotAllowed", "The project doesn't have this label, and only a project owner signed in as a person can create labels.");

    public static readonly Error SyncEmpty =
        Error.Invalid("Tasks.SyncEmpty", "Send at least one task.");

    public static readonly Error SyncTooLarge =
        Error.Invalid("Tasks.SyncTooLarge", "A sync can carry at most 200 tasks.");

    /// <summary>Returned inside the transaction when some item failed, so that nothing is saved. Not sent to clients.</summary>
    internal static readonly Error NotApplied =
        Error.Invalid("Tasks.SyncNotApplied", "Some tasks failed, so none was saved.");
}

public sealed record UpsertedTask(TaskItem Task, bool Created);

/// <summary>
/// Creates or updates a task by its external key and sets its dependencies; shared by the single upsert and the batch sync
/// (ADR 0017). The caller loads the project with <c>Work</c> access, calls these methods and saves. Status changes go through
/// <see cref="TaskMoves"/>, the code of the Kanban drag and drop.
/// </summary>
public sealed class TaskUpserter(IAppDbContext db, ICurrentUser currentUser, ActivityLog activity, IUniqueViolationDetector uniqueViolations, TimeProvider time)
{
    /// <summary>Applies the fields to the project's task with that key (created when there is none). Does not save.</summary>
    public async Task<Result<UpsertedTask>> ApplyAsync(Project project, string externalKey, TaskUpsertFields fields, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(fields);

        var key = externalKey.Trim();
        var task = await db.Tasks.Include(task => task.Dependencies).Include(task => task.Labels)
            .FirstOrDefaultAsync(task => task.ProjectId == project.Id && task.ExternalKey == key, cancellationToken);
        var applied = task is null
            ? await CreateAsync(project, key, fields, cancellationToken)
            : await UpdateAsync(project, task, key, fields, cancellationToken);
        if (!applied.IsSuccess || fields.Labels is null)
        {
            return applied;
        }

        var labelled = await ApplyLabelNamesAsync(project, applied.Value.Task, fields.Labels, cancellationToken);
        return labelled.IsSuccess ? applied : labelled.Error;
    }

    /// <summary>
    /// Replaces the task's labels with the project's labels of those names (case-insensitive). A label that doesn't exist is
    /// created with the rules of creating a label by hand: it needs <see cref="ProjectAccessLevel.Manage"/>, which an API key
    /// never has (ADR 0016).
    /// </summary>
    private async Task<Result> ApplyLabelNamesAsync(Project project, TaskItem task, IReadOnlyList<string> names, CancellationToken cancellationToken)
    {
        var wanted = names.Select(name => name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        // A sync saves after each task, so a label created for an earlier task is already in the database here.
        var existing = await db.Labels.Where(label => label.ProjectId == project.Id).ToListAsync(cancellationToken);

        var labels = new List<Label>();
        foreach (var name in wanted)
        {
            var label = existing.Find(other => string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase));
            if (label is null)
            {
                if (!ProjectAccess.Allows(project, ProjectAccessLevel.Manage, currentUser))
                {
                    return TaskSyncErrors.LabelCreateNotAllowed;
                }

                var created = Label.Create(project, name, NewLabelColor, existing, time.GetUtcNow());
                if (!created.IsSuccess)
                {
                    return created.Error;
                }

                label = created.Value;
                activity.Record(
                    project.Id,
                    ActivityType.LabelCreated,
                    new { labelId = label.Id, labelName = label.Name, color = label.Color });
                db.Labels.Add(label);
                existing.Add(label);
            }

            labels.Add(label);
        }

        return task.SetLabels(project, labels);
    }

    /// <summary>The color of a label created by an upsert; it can be changed afterwards.</summary>
    private const string NewLabelColor = "#6B7280";

    /// <summary>
    /// Sets the dependencies given as external keys. <paramref name="known"/> holds the tasks already at hand (case-insensitive
    /// keys); the rest are read from the project. <paramref name="edges"/> is the project's dependency graph, kept up to date here.
    /// </summary>
    public async Task<Result> ApplyDependenciesAsync(
        Project project,
        TaskItem task,
        IReadOnlyList<string> keys,
        IDictionary<string, TaskItem> known,
        List<(Guid TaskId, Guid DependsOnId)> edges,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(known);
        ArgumentNullException.ThrowIfNull(edges);

        var wanted = keys.Select(key => key.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var missing = wanted.Where(key => !known.ContainsKey(key)).ToList();
        if (missing.Count > 0)
        {
            var found = await db.Tasks
                .Where(other => other.ProjectId == project.Id && other.ExternalKey != null && missing.Contains(other.ExternalKey))
                .ToListAsync(cancellationToken);
            foreach (var other in found)
            {
                known[other.ExternalKey!] = other;
            }
        }

        var resolved = new List<TaskItem>();
        foreach (var key in wanted)
        {
            if (!known.TryGetValue(key, out var dependency))
            {
                return TaskSyncErrors.DependencyKeyNotFound;
            }

            resolved.Add(dependency);
        }

        var result = task.SetDependencies(project, resolved, edges, time.GetUtcNow());
        if (result.IsSuccess)
        {
            edges.RemoveAll(edge => edge.TaskId == task.Id);
            edges.AddRange(task.Dependencies.Select(dependency => (dependency.TaskItemId, dependency.DependsOnTaskId)));
        }

        return result;
    }

    /// <summary>The dependency graph of the project as (task, depends on) pairs.</summary>
    public async Task<List<(Guid TaskId, Guid DependsOnId)>> LoadEdgesAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var rows = await db.TaskDependencies.AsNoTracking()
            .Where(dependency => db.Tasks.Any(task => task.Id == dependency.TaskItemId && task.ProjectId == projectId))
            .Select(dependency => new { dependency.TaskItemId, dependency.DependsOnTaskId })
            .ToListAsync(cancellationToken);
        return rows.Select(row => (row.TaskItemId, row.DependsOnTaskId)).ToList();
    }

    /// <summary>Saves, turning a clash on the external key (a concurrent request used it first) into an error.</summary>
    public async Task<Result> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateException exception) when (uniqueViolations.IsUniqueViolation(exception))
        {
            return TaskErrors.ExternalKeyTaken;
        }
    }

    private async Task<Result<UpsertedTask>> CreateAsync(Project project, string key, TaskUpsertFields fields, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var lastPosition = await db.Tasks
            .Where(task => task.ProjectId == project.Id && task.Status == TaskItemStatus.ToDo)
            .MaxAsync(task => (int?)task.Position, cancellationToken);

        var created = TaskItem.Create(
            project,
            fields.Title.Trim(),
            fields.Description.TrimToNull(),
            fields.Priority,
            fields.AssigneeId,
            null,
            currentUser.Id,
            lastPosition + 1 ?? 0,
            now,
            fields.EstimatedMinutes);
        if (!created.IsSuccess)
        {
            return created.Error;
        }

        var task = created.Value;
        var planned = task.SetSchedule(project, fields.StartDate, fields.DueDate, now);
        if (!planned.IsSuccess)
        {
            return planned.Error;
        }

        planned = task.SetPlanning(project, key, fields.StartDate, fields.IsBlocked, fields.StatusNote, now);
        if (!planned.IsSuccess)
        {
            return planned.Error;
        }

        db.Tasks.Add(task);

        if (fields.Status is { } status && status != TaskItemStatus.ToDo)
        {
            var moved = await TaskMoves.MoveAsync(db, time, project, task, status, int.MaxValue, cancellationToken);
            if (!moved.IsSuccess)
            {
                return moved.Error;
            }
        }

        return new UpsertedTask(task, true);
    }

    private async Task<Result<UpsertedTask>> UpdateAsync(Project project, TaskItem task, string key, TaskUpsertFields fields, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var result = task.SetSchedule(project, fields.StartDate, fields.DueDate, now);
        if (result.IsSuccess)
        {
            result = task.Update(
                project,
                fields.Title.Trim(),
                fields.Description.TrimToNull(),
                fields.Priority,
                fields.AssigneeId,
                fields.DueDate,
                now,
                fields.EstimatedMinutes);
        }

        if (result.IsSuccess)
        {
            result = task.SetPlanning(project, key, fields.StartDate, fields.IsBlocked, fields.StatusNote, now);
        }

        if (!result.IsSuccess)
        {
            return result.Error;
        }

        if (fields.Status is { } status && status != task.Status)
        {
            var moved = await TaskMoves.MoveAsync(db, time, project, task, status, int.MaxValue, cancellationToken);
            if (!moved.IsSuccess)
            {
                return moved.Error;
            }
        }

        return new UpsertedTask(task, false);
    }
}
