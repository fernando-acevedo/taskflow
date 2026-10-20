using FluentValidation;
using TaskFlow.Application.Common;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Users;

public sealed record ListUsersQuery(string? Search, int Page = 1, int PageSize = 20);

public sealed class ListUsersValidator : AbstractValidator<ListUsersQuery>
{
    public ListUsersValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.Search).MaximumLength(100);
    }
}

public sealed class ListUsersHandler(IIdentityService identity, IValidator<ListUsersQuery> validator)
{
    public async Task<Result<PagedResult<UserDto>>> HandleAsync(ListUsersQuery query, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        return await identity.ListAsync(query.Search?.Trim(), query.Page, query.PageSize, cancellationToken);
    }
}
