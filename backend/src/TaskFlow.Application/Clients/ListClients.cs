using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Clients;

public sealed record ListClientsQuery(string? Search, bool IncludeArchived = false, int Page = 1, int PageSize = 50);

public sealed class ListClientsValidator : AbstractValidator<ListClientsQuery>
{
    public ListClientsValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.Search).MaximumLength(100);
    }
}

/// <summary>Every signed-in user can list clients, since creating a project means picking one.</summary>
public sealed class ListClientsHandler(IAppDbContext db, IValidator<ListClientsQuery> validator)
{
    public async Task<Result<PagedResult<ClientDto>>> HandleAsync(ListClientsQuery query, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        var clients = db.Clients.AsNoTracking();
        if (!query.IncludeArchived)
        {
            clients = clients.Where(client => !client.IsArchived);
        }

        var search = query.Search.TrimToNull();
        if (search is not null)
        {
            clients = clients.Where(client => client.Name.Contains(search));
        }

        return await clients
            .OrderBy(client => client.Name)
            .ToDtos(db.Projects)
            .ToPagedResultAsync(query.Page, query.PageSize, cancellationToken);
    }
}
