using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Demo;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Users;

public sealed record SetUserActiveCommand(Guid UserId, bool IsActive);

public sealed class SetUserActiveHandler(IIdentityService identity, ICurrentUser currentUser, IDemoMode demo)
{
    public async Task<Result> HandleAsync(SetUserActiveCommand command, CancellationToken cancellationToken)
    {
        if (command.UserId == currentUser.Id)
        {
            return UserErrors.CannotChangeOwnStatus;
        }

        var user = await identity.FindByIdAsync(command.UserId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound(command.UserId);
        }

        if (!command.IsActive && demo.IsEnabled && demo.IsProtectedAccount(user.Email))
        {
            return DemoErrors.AccountProtected;
        }

        await identity.SetActiveAsync(command.UserId, command.IsActive, cancellationToken);
        return Result.Success();
    }
}
