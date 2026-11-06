using TaskFlow.Domain.ApiKeys;
using TaskFlow.Domain.Tenancy;

namespace TaskFlow.Application.Abstractions;

/// <summary>The authenticated user of the current request, with the tenant resolved for it (ADR 0013).</summary>
public interface ICurrentUser
{
    Guid Id { get; }

    /// <summary>Platform administrator (the Identity role "Admin"). Acts as Owner in the tenant it chooses.</summary>
    bool IsSuperAdmin { get; }

    /// <summary>The tenant of this request; <c>null</c> when none was resolved.</summary>
    Guid? TenantId { get; }

    /// <summary>The user's role in <see cref="TenantId"/>; Owner for a super-admin; <c>null</c> without a tenant.</summary>
    TenantRole? TenantRole { get; }

    /// <summary>Owner or Admin of the current tenant (what the global Admin was before ADR 0013).</summary>
    bool IsTenantAdmin => TenantRole is Domain.Tenancy.TenantRole.Owner or Domain.Tenancy.TenantRole.Admin;

    /// <summary>The API key that authenticated this request; <c>null</c> when a person signed in (ADR 0016).</summary>
    Guid? ApiKeyId => null;

    /// <summary>The scope of <see cref="ApiKeyId"/>; <c>null</c> for a person.</summary>
    ApiKeyScope? ApiKeyScope => null;
}
