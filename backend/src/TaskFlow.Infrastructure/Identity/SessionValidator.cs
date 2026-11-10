using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using TaskFlow.Infrastructure.Caching;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Infrastructure.Identity;

/// <summary>
/// Checks that an access token still belongs to a live session. Bearer tokens are self-contained, so on their own
/// they stay valid until they expire even after the user is deactivated or loses a role. Those changes renew the
/// security stamp, and a token whose stamp is no longer the user's is rejected here. The current stamp is cached
/// briefly under the user's tag; changing the stamp drops the tag (see <see cref="ForgetAsync"/>). With a shared
/// cache (Redis) every instance sees that at once; with only the per-instance cache, other instances pick the
/// change up within <see cref="CacheDuration"/> (ADR 0020).
/// </summary>
public sealed class SessionValidator(AppDbContext db, HybridCache cache, IOptions<IdentityOptions> identityOptions)
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private static readonly HybridCacheEntryOptions EntryOptions = new() { Expiration = CacheDuration, LocalCacheExpiration = CacheDuration };

    public async Task<bool> IsCurrentAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var claimTypes = identityOptions.Value.ClaimsIdentity;
        if (!Guid.TryParse(principal.FindFirstValue(claimTypes.UserIdClaimType), out var userId))
        {
            return false;
        }

        // The cache holds a SHA-256 of the stamp, never the stamp itself (a shared cache such as Redis is outside the
        // process). An unknown user is cached as the empty string (a null value is not a good cache entry).
        var currentHash = await cache.GetOrCreateAsync(
            CacheKey(userId),
            async token => await db.Users.Where(user => user.Id == userId).Select(user => user.SecurityStamp).FirstOrDefaultAsync(token) is { } stamp
                ? Hash(stamp)
                : string.Empty,
            EntryOptions,
            [CacheTags.User(userId)],
            cancellationToken);

        return currentHash.Length > 0
            && principal.FindFirstValue(claimTypes.SecurityStampClaimType) is { } presented
            && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(currentHash), Encoding.ASCII.GetBytes(Hash(presented)));
    }

    /// <summary>
    /// Called after renewing a user's security stamp or changing whether they are active, so their old tokens are
    /// rejected at once. The tag also covers the cached "owner is active" check of their API keys.
    /// </summary>
    public ValueTask ForgetAsync(Guid userId, CancellationToken cancellationToken = default) =>
        cache.RemoveByTagAsync(CacheTags.User(userId), cancellationToken);

    private static string Hash(string stamp) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stamp)));

    private static string CacheKey(Guid userId) => $"security-stamp:{userId}";
}
