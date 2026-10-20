using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Application.Projects;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Tasks;

/// <summary>Filters combine with AND; <paramref name="Overdue"/> keeps only overdue tasks when true.</summary>
public sealed record ListTasksQuery(
    Guid ProjectId,
    TaskItemStatus? Status = null,
    TaskPriority? Priority = null,
    Guid? AssigneeId = null,
    bool Overdue = false,
    string? Search = null);

public sealed class ListTasksValidator : AbstractValidator<ListTasksQuery>
{
    public ListTasksValidator()
    {
        RuleFor(query => query.Status).IsInEnum();
        RuleFor(query => query.Priority).IsInEnum();
        RuleFor(query => query.Search).MaximumLength(100);
    }
}

/// <summary>
/// All matching tasks of a project in board order (status, then position). Not paged: the board shows a whole
/// project at once, and projects stay in the hundreds of tasks.
/// </summary>
public sealed class ListTasksHandler(IAppDbContext db, ProjectAccess access, IValidator<ListTasksQuery> validator, TimeProvider time)
{
    public async Task<Result<IReadOnlyList<TaskDto>>> HandleAsync(ListTasksQuery query, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        var allowed = await access.GetAsync(query.ProjectId, ProjectAccessLevel.View, cancellationToken);
        if (!allowed.IsSuccess)
        {
            return allowed.Error;
        }

        var today = time.Today();
        var tasks = db.Tasks.AsNoTracking().Where(task => task.ProjectId == query.ProjectId);
        if (query.Status is { } status)
        {
            tasks = tasks.Where(task => task.Status == status);
        }

        if (query.Priority is { } priority)
        {
            tasks = tasks.Where(task => task.Priority == priority);
        }

        if (query.AssigneeId is { } assigneeId)
        {
            tasks = tasks.Where(task => task.AssigneeId == assigneeId);
        }

        if (query.Overdue)
        {
            tasks = tasks.Where(TaskQueries.IsOverdue(today));
        }

        var search = query.Search.TrimToNull();
        if (search is not null)
        {
            tasks = tasks.Where(task => task.Title.Contains(search));
        }

        var list = await tasks
            .OrderBy(task => task.Status)
            .ThenBy(task => task.Position)
            .ToDtos(today)
            .ToListAsync(cancellationToken);
        return list;
    }
}
