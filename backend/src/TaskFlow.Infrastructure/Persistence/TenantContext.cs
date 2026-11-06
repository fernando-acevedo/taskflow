using TaskFlow.Application.Abstractions;

namespace TaskFlow.Infrastructure.Persistence;

/// <summary>
/// Scoped holder of the request's tenant. Nothing sets it yet: the request pipeline (X-Tenant header, T-60) and
/// background work (<c>TenantScope</c>, ADR 0014) call <see cref="Use"/>.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }

    /// <summary>Fixes the tenant for the rest of this scope.</summary>
    public void Use(Guid? tenantId) => TenantId = tenantId;
}
