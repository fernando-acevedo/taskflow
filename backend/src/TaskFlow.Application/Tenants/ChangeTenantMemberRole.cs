using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Application.Demo;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Tenancy;

namespace TaskFlow.Application.Tenants;

public sealed record ChangeTenantMemberRoleCommand(Guid UserId, TenantRole Role);

public sealed class ChangeTenantMemberRoleValidator : AbstractValidator<ChangeTenantMemberRoleCommand>
{
    public ChangeTenantMemberRoleValidator() => RuleFor(command => command.Role).IsInEnum();
}

/// <summary>
/// Changes a member's role. The last Owner can't be demoted (checked and written in one Serializable transaction),
/// and only an Owner touches Owners.
/// </summary>
public sealed class ChangeTenantMemberRoleHandler(
    IAppDbContext db,
    IIdentityService identity,
    ICurrentUser currentUser,
    IValidator<ChangeTenantMemberRoleCommand> validator,
    ITenantAccessCache accessCache,
    IDemoMode demo,
    IRealtimeRevoker realtime,
    ITransactionRunner transactions)
{
    public async Task<Result> HandleAsync(ChangeTenantMemberRoleCommand command, CancellationToken cancellationToken)
    {
        if (TenantRules.RequireTenantAdmin(currentUser) is { } denied)
        {
            return denied;
        }

        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        if (command.UserId == currentUser.Id)
        {
            return UserErrors.CannotChangeOwnRole;
        }

        var tenantId = currentUser.TenantId!.Value;
        var result = await transactions.RunSerializableAsync(token => ChangeAsync(command, tenantId, token), cancellationToken);
        if (result.IsSuccess)
        {
            await accessCache.ForgetAsync(command.UserId, cancellationToken);
            await realtime.TenantAccessChangedAsync(tenantId, command.UserId, cancellationToken);
        }

        return result;
    }

    private async Task<Result> ChangeAsync(ChangeTenantMemberRoleCommand command, Guid tenantId, CancellationToken cancellationToken)
    {
        var membership = await db.TenantMemberships.FirstOrDefaultAsync(
            member => member.TenantId == tenantId && member.UserId == command.UserId, cancellationToken);
        if (membership is null)
        {
            return TenantErrors.MemberNotFound;
        }

        if (membership.Role == command.Role)
        {
            return Result.Success();
        }

        var touchesOwner = membership.Role == TenantRole.Owner || command.Role == TenantRole.Owner;
        if (touchesOwner && !TenantRules.IsOwner(currentUser))
        {
            return TenantErrors.OwnerRequired;
        }

        if (membership.Role == TenantRole.Owner
            && await db.TenantMemberships.CountAsync(
                member => member.TenantId == tenantId && member.Role == TenantRole.Owner, cancellationToken) <= 1)
        {
            return TenantErrors.LastOwner;
        }

        if (demo.IsEnabled
            && await identity.FindByIdAsync(command.UserId, cancellationToken) is { } user
            && demo.IsProtectedAccount(user.Email))
        {
            return DemoErrors.AccountProtected;
        }

        membership.ChangeRole(command.Role);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
