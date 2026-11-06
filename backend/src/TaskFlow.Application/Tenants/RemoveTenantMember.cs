using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Demo;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Tenancy;

namespace TaskFlow.Application.Tenants;

public sealed record RemoveTenantMemberCommand(Guid UserId);

/// <summary>Removes a membership; the account and its other tenants stay. The last Owner can't be removed.</summary>
public sealed class RemoveTenantMemberHandler(
    IAppDbContext db,
    IIdentityService identity,
    ICurrentUser currentUser,
    ITenantAccessCache accessCache,
    IDemoMode demo)
{
    public async Task<Result> HandleAsync(RemoveTenantMemberCommand command, CancellationToken cancellationToken)
    {
        if (TenantRules.RequireTenantAdmin(currentUser) is { } denied)
        {
            return denied;
        }

        var tenantId = currentUser.TenantId!.Value;
        var membership = await db.TenantMemberships.FirstOrDefaultAsync(
            member => member.TenantId == tenantId && member.UserId == command.UserId, cancellationToken);
        if (membership is null)
        {
            return TenantErrors.MemberNotFound;
        }

        if (membership.Role == TenantRole.Owner)
        {
            if (!TenantRules.IsOwner(currentUser))
            {
                return TenantErrors.OwnerRequired;
            }

            var owners = await db.TenantMemberships.CountAsync(
                member => member.TenantId == tenantId && member.Role == TenantRole.Owner, cancellationToken);
            if (owners <= 1)
            {
                return TenantErrors.LastOwner;
            }
        }

        if (demo.IsEnabled
            && await identity.FindByIdAsync(command.UserId, cancellationToken) is { } user
            && demo.IsProtectedAccount(user.Email))
        {
            return DemoErrors.AccountProtected;
        }

        db.TenantMemberships.Remove(membership);
        await db.SaveChangesAsync(cancellationToken);
        accessCache.Forget(command.UserId, tenantId);
        return Result.Success();
    }
}
