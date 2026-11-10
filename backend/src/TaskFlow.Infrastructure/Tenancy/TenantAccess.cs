using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Memory;
using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Common;
using TaskFlow.Infrastructure.Caching;
using TaskFlow.Domain.Tenancy;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Infrastructure.Tenancy;

/// <summary>What a user may do in a tenant. <see cref="Role"/> is <c>null</c> when they do not belong to it.</summary>
public readonly record struct TenantAccessResult(bool TenantExists, TenantRole? Role)
{
    /// <summary>A super-admin is in a tenant they do not belong to (support access, ADR 0013).</summary>
    public bool IsSupportAccess { get; init; }
}

/// <summary>
/// Checks the user's membership in a tenant (ADR 0013). The answer is cached per (user, tenant) for
/// <see cref="CacheDuration"/> under the user's membership tag; a membership change drops the tag (see
/// <see cref="ForgetAsync"/>), which every instance sees at once with a shared cache (Redis) and within the cache
/// duration without one (ADR 0020).
/// </summary>
public sealed class TenantAccess(AppDbContext db, HybridCache cache, IMemoryCache localCache, TimeProvider? time = null) : ITenantAccessCache
{
    /// <summary>At most one support-access row per (user, tenant) in this window.</summary>
    public static readonly TimeSpan SupportAccessWindow = TimeSpan.FromMinutes(30);

    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    /// <summary>Resolves an explicit tenant. A super-admin gets Owner in any existing tenant they do not belong to.</summary>
    public async Task<TenantAccessResult> CheckAsync(Guid userId, Guid tenantId, bool isSuperAdmin, CancellationToken cancellationToken)
    {
        var entry = await cache.GetOrCreateAsync(AccessKey(userId, tenantId), async token =>
        {
            var exists = await db.Tenants.AnyAsync(tenant => tenant.Id == tenantId, token);
            TenantRole? role = await db.TenantMemberships
                .Where(membership => membership.UserId == userId && membership.TenantId == tenantId)
                .Select(membership => (TenantRole?)membership.Role)
                .FirstOrDefaultAsync(token);
            return new TenantAccessResult(exists, role);
        },
        EntryOptions,
        [CacheTags.Membership(userId)],
        cancellationToken);

        // A super-admin who belongs to the tenant acts with their real role there; the implicit Owner is only for
        // tenants they do not belong to (support access).
        return isSuperAdmin && entry.TenantExists && entry.Role is null
            ? entry with { Role = TenantRole.Owner, IsSupportAccess = true }
            : entry;
    }

    /// <summary>
    /// The tenant to use when the request names none: the user's only one. With several (or none) there is no
    /// default and the request stays without a tenant.
    /// </summary>
    public async Task<(Guid TenantId, TenantRole Role)?> DefaultAsync(Guid userId, CancellationToken cancellationToken)
    {
        var memberships = await cache.GetOrCreateAsync(DefaultKey(userId), async token =>
            await db.TenantMemberships
                .Where(membership => membership.UserId == userId)
                .Select(membership => new DefaultMembership(membership.TenantId, membership.Role))
                .Take(2)
                .ToListAsync(token),
            EntryOptions,
            [CacheTags.Membership(userId)],
            cancellationToken);

        return memberships is { Count: 1 } ? (memberships[0].TenantId, memberships[0].Role) : null;
    }

    /// <summary>Records that a super-admin entered a tenant they do not belong to, at most once per window (same cache).</summary>
    public async Task RecordSupportAccessAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
    {
        // Per instance on purpose: at worst each instance writes one audit row per window.
        var key = $"tenant-support:{userId}:{tenantId}";
        if (localCache.TryGetValue(key, out _))
        {
            return;
        }

        db.TenantSupportAccesses.Add(TenantSupportAccess.Create(tenantId, userId, (time ?? TimeProvider.System).GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        localCache.Set(key, true, SupportAccessWindow);
    }

    /// <summary>Called after adding, removing or changing a membership, so the user's cached access is dropped at once.</summary>
    public ValueTask ForgetAsync(Guid userId, CancellationToken cancellationToken = default) =>
        cache.RemoveByTagAsync(CacheTags.Membership(userId), cancellationToken);

    private static readonly HybridCacheEntryOptions EntryOptions = new() { Expiration = CacheDuration, LocalCacheExpiration = CacheDuration };

    private sealed record DefaultMembership(Guid TenantId, TenantRole Role);

    private static string AccessKey(Guid userId, Guid tenantId) => $"tenant-access:{userId}:{tenantId}";

    private static string DefaultKey(Guid userId) => $"tenant-default:{userId}";

    /// <summary>Maps the outcome for an explicit tenant to the error the request answers with.</summary>
    public static Error? ErrorFor(TenantAccessResult access) =>
        !access.TenantExists ? TenantErrors.NotFound : access.Role is null ? TenantErrors.Forbidden : null;
}
