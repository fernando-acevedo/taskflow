using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Users;

/// <summary>One user, for admins.</summary>
public sealed class GetUserHandler(IIdentityService identity)
{
    public async Task<Result<UserDto>> HandleAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await identity.FindByIdAsync(userId, cancellationToken);
        return user is null ? UserErrors.NotFound(userId) : user;
    }
}
