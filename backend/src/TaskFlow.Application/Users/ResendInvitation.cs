using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Users;

public sealed record ResendInvitationCommand(Guid UserId);

/// <summary>Sends a fresh invitation link to a user who hasn't chosen a password yet.</summary>
public sealed class ResendInvitationHandler(IIdentityService identity, IInvitationSender invitations)
{
    public async Task<Result> HandleAsync(ResendInvitationCommand command, CancellationToken cancellationToken)
    {
        var user = await identity.FindByIdAsync(command.UserId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound(command.UserId);
        }

        if (!user.IsInvitationPending)
        {
            return UserErrors.InvitationNotPending;
        }

        var token = await identity.GenerateInvitationTokenAsync(user.Id, cancellationToken);
        return await invitations.SendAsync(user, token, cancellationToken) ? Result.Success() : UserErrors.InvitationNotSent;
    }
}
