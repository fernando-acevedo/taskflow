using System.Linq.Expressions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Labels;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Tasks;

/// <param name="IsOverdue">Past its due date (UTC) and not done.</param>
public sealed record TaskDto(
    Guid Id,
    Guid ProjectId,
    string Title,
    string? Description,
    TaskItemStatus Status,
    TaskPriority Priority,
    Guid? AssigneeId,
    DateOnly? DueDate,
    int? EstimatedMinutes,
    bool IsOverdue,
    int Position,
    Guid CreatedById,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    byte[] RowVersion,
    IReadOnlyList<LabelDto> Labels,
    string? ExternalKey,
    DateOnly? StartDate,
    bool IsBlocked,
    string? StatusNote,
    IReadOnlyList<Guid> DependsOn);

/// <summary>The editable fields of a task, shared by create and update.</summary>
public interface ITaskFields
{
    string Title { get; }

    string? Description { get; }

    TaskPriority Priority { get; }

    Guid? AssigneeId { get; }

    DateOnly? DueDate { get; }

    int? EstimatedMinutes { get; }

    /// <summary>Labels to put on the task; null leaves them as they are (on create, no labels).</summary>
    IReadOnlyList<Guid>? LabelIds { get; }
}

internal static class TaskQueries
{
    public static void AddTaskFieldRules<T>(this AbstractValidator<T> validator)
        where T : ITaskFields
    {
        validator.RuleFor(fields => fields.Title).NotEmpty().MaximumLength(TaskItem.TitleMaxLength);
        validator.RuleFor(fields => fields.Description).MaximumLength(TaskItem.DescriptionMaxLength);
        validator.RuleFor(fields => fields.Priority).IsInEnum();
        validator.RuleFor(fields => fields.EstimatedMinutes).InclusiveBetween(0, MaxEstimatedMinutes);
        validator.RuleFor(fields => fields.LabelIds).Must(ids => ids!.Count <= MaxLabelsPerTask)
            .When(fields => fields.LabelIds is not null);
    }

    public const int MaxLabelsPerTask = 20;

    public const int MaxEstimatedMinutes = 9999 * 60;

    /// <summary>Sets the task's labels from the ids, which must all be labels of the project. Null changes nothing.</summary>
    public static async Task<Result> ApplyLabelsAsync(
        this TaskItem task,
        IAppDbContext db,
        Project project,
        IReadOnlyList<Guid>? labelIds,
        CancellationToken cancellationToken)
    {
        if (labelIds is null)
        {
            return Result.Success();
        }

        var wanted = labelIds.Distinct().ToList();
        var labels = await db.Labels
            .Where(label => label.ProjectId == project.Id && wanted.Contains(label.Id))
            .ToListAsync(cancellationToken);
        if (labels.Count != wanted.Count)
        {
            return LabelErrors.NotInProject;
        }

        return task.SetLabels(project, labels);
    }

    public static DateOnly Today(this TimeProvider time) => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

    public static Expression<Func<TaskItem, bool>> IsOverdue(DateOnly today) =>
        task => task.DueDate < today && task.Status != TaskItemStatus.Done;

    public static IQueryable<TaskDto> ToDtos(this IQueryable<TaskItem> tasks, DateOnly today) =>
        tasks.Select(task => new TaskDto(
            task.Id,
            task.ProjectId,
            task.Title,
            task.Description,
            task.Status,
            task.Priority,
            task.AssigneeId,
            task.DueDate,
            task.EstimatedMinutes,
            task.DueDate < today && task.Status != TaskItemStatus.Done,
            task.Position,
            task.CreatedById,
            task.CreatedAt,
            task.UpdatedAt,
            task.CompletedAt,
            task.RowVersion,
            task.Labels.OrderBy(label => label.Name).Select(label => new LabelDto(label.Id, label.Name, label.Color, label.RowVersion)).ToList(),
            task.ExternalKey,
            task.StartDate,
            task.IsBlocked,
            task.StatusNote,
            task.Dependencies.Select(dependency => dependency.DependsOnTaskId).ToList()));
}
