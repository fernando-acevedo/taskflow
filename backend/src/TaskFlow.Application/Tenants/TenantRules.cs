using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Tenancy;

namespace TaskFlow.Application.Tenants;

internal static class TenantRules
{
    public static readonly Error SuperAdminRequired =
        Error.Forbidden("Tenants.SuperAdminRequired", "Only a platform administrator can do this.");

    /// <summary>Managing members needs a resolved tenant and Owner or Admin in it (a super-admin counts as Owner).</summary>
    public static Error? RequireTenantAdmin(ICurrentUser currentUser) =>
        currentUser.TenantId is null || !currentUser.IsTenantAdmin ? TenantErrors.Forbidden : null;

    /// <summary>Only an Owner may grant, change or remove the Owner role.</summary>
    public static bool IsOwner(ICurrentUser currentUser) => currentUser.TenantRole == TenantRole.Owner;
}
