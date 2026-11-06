using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Tenants;

/// <summary>Every tenant of the platform, for the super-admin.</summary>
public sealed class ListTenantsHandler(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<Result<IReadOnlyList<TenantDto>>> HandleAsync(CancellationToken cancellationToken)
    {
        if (!currentUser.IsSuperAdmin)
        {
            return TenantRules.SuperAdminRequired;
        }

        IReadOnlyList<TenantDto> tenants = await db.Tenants
            .OrderBy(tenant => tenant.Name)
            .Select(tenant => new TenantDto(tenant.Id, tenant.Name, tenant.Slug, tenant.IsDemo, tenant.CreatedAt))
            .ToListAsync(cancellationToken);
        return Result<IReadOnlyList<TenantDto>>.Success(tenants);
    }
}
