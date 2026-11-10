using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Demo;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Tenancy;

namespace TaskFlow.Application.Tenants;

public sealed record RemoveTenantMemberCommand(Guid UserId);

/// <summary>
/// Removes a membership; the account and its other tenants stay. The last Owner can't be removed. Everything happens
/// in one Serializable transaction (two concurrent removals can't leave the tenant without an Owner), and it also
/// takes the user out of the tenant's projects and clears them as assignee of its tasks, so they keep no access path.
/// If they were the only Owner of a project, the Owner of the tenant who runs the action (or else the tenant's oldest
/// other Owner) becomes that project's Owner.
/// </summary>
public sealed class RemoveTenantMemberHandler(
    IAppDbContext db,
    IIdentityService identity,
    ICurrentUser currentUser,
    ITenantAccessCache accessCache,
    IDemoMode demo,
    IRealtimeRevoker realtime,
    ITransactionRunner transactions,
    TimeProvider time)
{
    public async Task<Result> HandleAsync(RemoveTenantMemberCommand command, CancellationToken cancellationToken)
    {
        if (TenantRules.RequireTenantAdmin(currentUser) is { } denied)
        {
            return denied;
        }

        var tenantId = currentUser.TenantId!.Value;
        var result = await transactions.RunSerializableAsync(token => RemoveAsync(command.UserId, tenantId, token), cancellationToken);
        if (result.IsSuccess)
        {
            await accessCache.ForgetAsync(command.UserId, cancellationToken);
            await realtime.TenantAccessChangedAsync(tenantId, command.UserId, cancellationToken);
        }

        return result;
    }

    private async Task<Result> RemoveAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
    {
        var membership = await db.TenantMemberships.FirstOrDefaultAsync(
            member => member.TenantId == tenantId && member.UserId == userId, cancellationToken);
        if (membership is null)
        {
            return TenantErrors.MemberNotFound;
        }

        var owners = await db.TenantMemberships
            .Where(member => member.TenantId == tenantId && member.Role == TenantRole.Owner)
            .OrderBy(member => member.JoinedAt)
            .ToListAsync(cancellationToken);
        if (membership.Role == TenantRole.Owner)
        {
            if (!TenantRules.IsOwner(currentUser))
            {
                return TenantErrors.OwnerRequired;
            }

            if (owners.Count <= 1)
            {
                return TenantErrors.LastOwner;
            }
        }

        if (demo.IsEnabled
            && await identity.FindByIdAsync(userId, cancellationToken) is { } user
            && demo.IsProtectedAccount(user.Email))
        {
            return DemoErrors.AccountProtected;
        }

        var now = time.GetUtcNow();
        var others = owners.Where(owner => owner.UserId != userId).ToList();
        var successorId = others.Any(owner => owner.UserId == currentUser.Id)
            ? currentUser.Id
            : others.Select(owner => owner.UserId).FirstOrDefault();

        var projects = await db.Projects.Include(project => project.Members)
            .Where(project => project.Members.Any(member => member.UserId == userId))
            .ToListAsync(cancellationToken);
        foreach (var project in projects)
        {
            project.Offboard(userId, successorId, now);
        }

        var tasks = await db.Tasks.Where(task => task.AssigneeId == userId).ToListAsync(cancellationToken);
        foreach (var task in tasks)
        {
            task.Unassign(now);
        }

        db.TenantMemberships.Remove(membership);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
