using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Tenants;

/// <summary>The tenants the current user belongs to; needs no resolved tenant.</summary>
public sealed class ListMyTenantsHandler(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<Result<IReadOnlyList<MyTenantDto>>> HandleAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.Id;
        IReadOnlyList<MyTenantDto> tenants = await db.TenantMemberships
            .Where(membership => membership.UserId == userId)
            .Join(
                db.Tenants,
                membership => membership.TenantId,
                tenant => tenant.Id,
                (membership, tenant) => new MyTenantDto(tenant.Id, tenant.Name, tenant.Slug, tenant.IsDemo, membership.Role))
            .OrderBy(tenant => tenant.Name)
            .ToListAsync(cancellationToken);
        return Result<IReadOnlyList<MyTenantDto>>.Success(tenants);
    }
}
