using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Demo;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Users;

public sealed record SetUserAdminCommand(Guid UserId, bool IsAdmin);

public sealed class SetUserAdminHandler(IIdentityService identity, ICurrentUser currentUser, IDemoMode demo)
{
    public async Task<Result> HandleAsync(SetUserAdminCommand command, CancellationToken cancellationToken)
    {
        if (command.UserId == currentUser.Id)
        {
            return UserErrors.CannotChangeOwnRole;
        }

        var user = await identity.FindByIdAsync(command.UserId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound(command.UserId);
        }

        if (!command.IsAdmin && demo.IsEnabled && demo.IsProtectedAccount(user.Email))
        {
            return DemoErrors.AccountProtected;
        }

        await identity.SetAdminAsync(command.UserId, command.IsAdmin, cancellationToken);
        return Result.Success();
    }
}
