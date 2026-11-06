using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Tenants;

/// <summary>The members of the current tenant, Owners first.</summary>
public sealed class ListTenantMembersHandler(IAppDbContext db, IIdentityService identity, ICurrentUser currentUser)
{
    public async Task<Result<IReadOnlyList<TenantMemberDto>>> HandleAsync(CancellationToken cancellationToken)
    {
        if (TenantRules.RequireTenantAdmin(currentUser) is { } denied)
        {
            return denied;
        }

        var tenantId = currentUser.TenantId!.Value;
        var memberships = await db.TenantMemberships.Where(membership => membership.TenantId == tenantId).ToListAsync(cancellationToken);
        var users = (await identity.FindByIdsAsync(memberships.Select(membership => membership.UserId).ToList(), cancellationToken))
            .ToDictionary(user => user.Id);

        IReadOnlyList<TenantMemberDto> members = memberships
            .Where(membership => users.ContainsKey(membership.UserId))
            .Select(membership => TenantMemberInviter.ToMember(users[membership.UserId], membership))
            .OrderByDescending(member => member.Role)
            .ThenBy(member => member.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return Result<IReadOnlyList<TenantMemberDto>>.Success(members);
    }
}
