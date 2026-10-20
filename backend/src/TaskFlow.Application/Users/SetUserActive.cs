using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Users;

public sealed record SetUserActiveCommand(Guid UserId, bool IsActive);

public sealed class SetUserActiveHandler(IIdentityService identity, ICurrentUser currentUser)
{
    public async Task<Result> HandleAsync(SetUserActiveCommand command, CancellationToken cancellationToken)
    {
        if (command.UserId == currentUser.Id)
        {
            return UserErrors.CannotChangeOwnStatus;
        }

        if (await identity.FindByIdAsync(command.UserId, cancellationToken) is null)
        {
            return UserErrors.NotFound(command.UserId);
        }

        await identity.SetActiveAsync(command.UserId, command.IsActive, cancellationToken);
        return Result.Success();
    }
}
