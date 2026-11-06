using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Application.Projects;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Activity;

public sealed record ListTaskActivityQuery(Guid ProjectId, Guid TaskId, int Page = 1, int PageSize = 50);

public sealed class ListTaskActivityValidator : AbstractValidator<ListTaskActivityQuery>
{
    public ListTaskActivityValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
    }
}

/// <summary>
/// One task's history, newest first. It doesn't check that the task still exists, so the history of a deleted
/// task stays readable.
/// </summary>
public sealed class ListTaskActivityHandler(
    IAppDbContext db,
    ProjectAccess access,
    IIdentityService identity,
    IValidator<ListTaskActivityQuery> validator)
{
    public async Task<Result<PagedResult<ActivityEntryDto>>> HandleAsync(ListTaskActivityQuery query, CancellationToken cancellationToken)
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

        var page = await db.ActivityEntries.AsNoTracking()
            .Where(entry => entry.ProjectId == query.ProjectId && entry.TaskItemId == query.TaskId)
            .OrderByDescending(entry => entry.OccurredAt)
            .ThenByDescending(entry => entry.Sequence)
            .ToPagedResultAsync(query.Page, query.PageSize, cancellationToken);

        return await page.ToPageAsync(identity, cancellationToken);
    }
}
