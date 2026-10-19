using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Users;

public sealed class GetCurrentUserHandler(IIdentityService identity, ICurrentUser currentUser)
{
    public async Task<Result<UserDto>> HandleAsync(CancellationToken cancellationToken)
    {
        var user = await identity.FindByIdAsync(currentUser.Id, cancellationToken);
        return user is null ? UserErrors.NotFound(currentUser.Id) : user;
    }
}
