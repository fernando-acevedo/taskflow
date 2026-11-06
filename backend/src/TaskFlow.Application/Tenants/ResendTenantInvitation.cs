using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Demo;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Tenancy;

namespace TaskFlow.Application.Tenants;

public sealed record ResendTenantInvitationCommand(Guid UserId);

/// <summary>Resends the password invitation of a member of the current tenant who hasn't accepted it yet.</summary>
public sealed class ResendTenantInvitationHandler(
    IAppDbContext db,
    IIdentityService identity,
    IInvitationSender invitations,
    ICurrentUser currentUser,
    IDemoMode demo)
{
    public async Task<Result> HandleAsync(ResendTenantInvitationCommand command, CancellationToken cancellationToken)
    {
        if (TenantRules.RequireTenantAdmin(currentUser) is { } denied)
        {
            return denied;
        }

        if (demo.IsEnabled)
        {
            return DemoErrors.ActionDisabled;
        }

        var tenantId = currentUser.TenantId!.Value;
        var isMember = await db.TenantMemberships.AnyAsync(
            member => member.TenantId == tenantId && member.UserId == command.UserId, cancellationToken);
        if (!isMember)
        {
            return TenantErrors.MemberNotFound;
        }

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
