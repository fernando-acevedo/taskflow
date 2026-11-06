using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Common;
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
/// <see cref="CacheDuration"/>; this instance forgets it as soon as a membership changes (see <see cref="Forget"/>),
/// other instances pick the change up within the cache duration.
/// </summary>
public sealed class TenantAccess(AppDbContext db, IMemoryCache cache, TimeProvider? time = null) : ITenantAccessCache
{
    /// <summary>At most one support-access row per (user, tenant) in this window.</summary>
    public static readonly TimeSpan SupportAccessWindow = TimeSpan.FromMinutes(30);

    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    /// <summary>Resolves an explicit tenant. A super-admin gets Owner in any existing tenant.</summary>
    public async Task<TenantAccessResult> CheckAsync(Guid userId, Guid tenantId, bool isSuperAdmin, CancellationToken cancellationToken)
    {
        var entry = await cache.GetOrCreateAsync(AccessKey(userId, tenantId), async cacheEntry =>
        {
            cacheEntry.AbsoluteExpirationRelativeToNow = CacheDuration;
            var exists = await db.Tenants.AnyAsync(tenant => tenant.Id == tenantId, cancellationToken);
            TenantRole? role = await db.TenantMemberships
                .Where(membership => membership.UserId == userId && membership.TenantId == tenantId)
                .Select(membership => (TenantRole?)membership.Role)
                .FirstOrDefaultAsync(cancellationToken);
            return new TenantAccessResult(exists, role);
        });

        return isSuperAdmin && entry.TenantExists
            ? entry with { Role = TenantRole.Owner, IsSupportAccess = entry.Role is null }
            : entry;
    }

    /// <summary>
    /// The tenant to use when the request names none: the user's only one. With several (or none) there is no
    /// default and the request stays without a tenant.
    /// </summary>
    public async Task<(Guid TenantId, TenantRole Role)?> DefaultAsync(Guid userId, CancellationToken cancellationToken)
    {
        var memberships = await cache.GetOrCreateAsync(DefaultKey(userId), async cacheEntry =>
        {
            cacheEntry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await db.TenantMemberships
                .Where(membership => membership.UserId == userId)
                .Select(membership => new { membership.TenantId, membership.Role })
                .Take(2)
                .ToListAsync(cancellationToken);
        });

        return memberships is { Count: 1 } ? (memberships[0].TenantId, memberships[0].Role) : null;
    }

    /// <summary>Records that a super-admin entered a tenant they do not belong to, at most once per window (same cache).</summary>
    public async Task RecordSupportAccessAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
    {
        var key = $"tenant-support:{userId}:{tenantId}";
        if (cache.TryGetValue(key, out _))
        {
            return;
        }

        db.TenantSupportAccesses.Add(TenantSupportAccess.Create(tenantId, userId, (time ?? TimeProvider.System).GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        cache.Set(key, true, SupportAccessWindow);
    }

    /// <summary>Called after adding, removing or changing a membership, so this instance sees it at once.</summary>
    public void Forget(Guid userId, Guid tenantId)
    {
        cache.Remove(AccessKey(userId, tenantId));
        cache.Remove(DefaultKey(userId));
    }

    private static string AccessKey(Guid userId, Guid tenantId) => $"tenant-access:{userId}:{tenantId}";

    private static string DefaultKey(Guid userId) => $"tenant-default:{userId}";

    /// <summary>Maps the outcome for an explicit tenant to the error the request answers with.</summary>
    public static Error? ErrorFor(TenantAccessResult access) =>
        !access.TenantExists ? TenantErrors.NotFound : access.Role is null ? TenantErrors.Forbidden : null;
}
