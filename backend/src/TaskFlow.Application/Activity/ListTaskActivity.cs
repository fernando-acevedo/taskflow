using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Application.Projects;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Activity;

public sealed record ListTaskActivityQuery(Guid ProjectId, Guid TaskId, string? Cursor = null, int PageSize = 50);

public sealed class ListTaskActivityValidator : AbstractValidator<ListTaskActivityQuery>
{
    public ListTaskActivityValidator()
    {
        RuleFor(query => query.Cursor).Must(PageCursor.IsValidSequence).WithMessage("The cursor is not valid.");
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
    public async Task<Result<CursorPage<ActivityEntryDto>>> HandleAsync(ListTaskActivityQuery query, CancellationToken cancellationToken)
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

        var entries = db.ActivityEntries.AsNoTracking()
            .Where(entry => entry.ProjectId == query.ProjectId && entry.TaskItemId == query.TaskId);

        if (PageCursor.TryDecode(query.Cursor, out var at, out var key))
        {
            var sequence = long.Parse(key, CultureInfo.InvariantCulture);
            entries = entries.Where(entry => entry.OccurredAt < at || (entry.OccurredAt == at && entry.Sequence < sequence));
        }

        var rows = await entries
            .OrderByDescending(entry => entry.OccurredAt)
            .ThenByDescending(entry => entry.Sequence)
            .Take(query.PageSize + 1)
            .ToListAsync(cancellationToken);

        return await rows.ToPageAsync(query.PageSize, identity, cancellationToken);
    }
}
