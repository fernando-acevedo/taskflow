using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Application.Projects;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Activity;

public sealed record ListProjectActivityQuery(Guid ProjectId, string? Cursor = null, int PageSize = 50);

public sealed class ListProjectActivityValidator : AbstractValidator<ListProjectActivityQuery>
{
    public ListProjectActivityValidator()
    {
        RuleFor(query => query.Cursor).Must(PageCursor.IsValidSequence).WithMessage("The cursor is not valid.");
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
    }
}

/// <summary>The project's history, newest first. Anyone who can see the project can read it.</summary>
public sealed class ListProjectActivityHandler(
    IAppDbContext db,
    ProjectAccess access,
    IIdentityService identity,
    IValidator<ListProjectActivityQuery> validator)
{
    public async Task<Result<CursorPage<ActivityEntryDto>>> HandleAsync(ListProjectActivityQuery query, CancellationToken cancellationToken)
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
            .Where(entry => entry.ProjectId == query.ProjectId);

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
