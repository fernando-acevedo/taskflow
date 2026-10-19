using System.Linq.Expressions;
using FluentValidation;
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
    bool IsOverdue,
    int Position,
    Guid CreatedById,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    byte[] RowVersion);

/// <summary>The editable fields of a task, shared by create and update.</summary>
public interface ITaskFields
{
    string Title { get; }

    string? Description { get; }

    TaskPriority Priority { get; }

    Guid? AssigneeId { get; }

    DateOnly? DueDate { get; }
}

internal static class TaskQueries
{
    public static void AddTaskFieldRules<T>(this AbstractValidator<T> validator)
        where T : ITaskFields
    {
        validator.RuleFor(fields => fields.Title).NotEmpty().MaximumLength(TaskItem.TitleMaxLength);
        validator.RuleFor(fields => fields.Description).MaximumLength(TaskItem.DescriptionMaxLength);
        validator.RuleFor(fields => fields.Priority).IsInEnum();
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
            task.DueDate < today && task.Status != TaskItemStatus.Done,
            task.Position,
            task.CreatedById,
            task.CreatedAt,
            task.UpdatedAt,
            task.CompletedAt,
            task.RowVersion));
}
