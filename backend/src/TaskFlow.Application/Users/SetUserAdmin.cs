using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Users;

public sealed record SetUserAdminCommand(Guid UserId, bool IsAdmin);

public sealed class SetUserAdminHandler(IIdentityService identity, ICurrentUser currentUser)
{
    public async Task<Result> HandleAsync(SetUserAdminCommand command, CancellationToken cancellationToken)
    {
        if (command.UserId == currentUser.Id)
        {
            return UserErrors.CannotChangeOwnRole;
        }

        if (await identity.FindByIdAsync(command.UserId, cancellationToken) is null)
        {
            return UserErrors.NotFound(command.UserId);
        }

        await identity.SetAdminAsync(command.UserId, command.IsAdmin, cancellationToken);
        return Result.Success();
    }
}
