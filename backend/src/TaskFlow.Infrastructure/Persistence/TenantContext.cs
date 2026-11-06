using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Tenancy;

namespace TaskFlow.Infrastructure.Persistence;

/// <summary>
/// Scoped holder of the request's tenant. The tenant middleware (X-Tenant header, ADR 0013) and background work
/// (<c>TenantScope</c>, ADR 0014) call <see cref="Use"/>.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }

    /// <summary>The current user's role in the tenant, when a request resolved it.</summary>
    public TenantRole? Role { get; private set; }

    /// <summary>Fixes the tenant for the rest of this scope.</summary>
    public void Use(Guid? tenantId, TenantRole? role = null)
    {
        TenantId = tenantId;
        Role = role;
    }
}
