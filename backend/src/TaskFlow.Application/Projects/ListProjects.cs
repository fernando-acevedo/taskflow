using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Projects;

public sealed record ListProjectsQuery(string? Search, Guid? ClientId, bool Archived = false, int Page = 1, int PageSize = 20);

public sealed class ListProjectsValidator : AbstractValidator<ListProjectsQuery>
{
    public ListProjectsValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.Search).MaximumLength(100);
    }
}

/// <summary>Lists the projects the user is a member of (admins see all), either active or archived ones.</summary>
public sealed class ListProjectsHandler(IAppDbContext db, ICurrentUser currentUser, IValidator<ListProjectsQuery> validator)
{
    public async Task<Result<PagedResult<ProjectDto>>> HandleAsync(ListProjectsQuery query, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        var userId = currentUser.Id;
        var projects = db.Projects.AsNoTracking().Where(project => project.IsArchived == query.Archived);
        if (!currentUser.IsAdmin)
        {
            projects = projects.Where(project => project.Members.Any(member => member.UserId == userId));
        }

        if (query.ClientId is { } clientId)
        {
            projects = projects.Where(project => project.ClientId == clientId);
        }

        var search = query.Search.TrimToNull();
        if (search is not null)
        {
            projects = projects.Where(project => project.Name.Contains(search));
        }

        return await projects
            .OrderByDescending(project => project.UpdatedAt)
            .ToDtos(db.Clients, userId)
            .ToPagedResultAsync(query.Page, query.PageSize, cancellationToken);
    }
}
