using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Infrastructure.Identity;

/// <summary>
/// Checks that an access token still belongs to a live session. Bearer tokens are self-contained, so on their own
/// they stay valid until they expire even after the user is deactivated or loses a role. Those changes renew the
/// security stamp, and a token whose stamp is no longer the user's is rejected here. The current stamp is cached
/// briefly; this instance forgets it as soon as it changes the stamp (see <see cref="Forget"/>), other instances
/// pick the change up within <see cref="CacheDuration"/>.
/// </summary>
public sealed class SessionValidator(AppDbContext db, IMemoryCache cache, IOptions<IdentityOptions> identityOptions)
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    public async Task<bool> IsCurrentAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var claimTypes = identityOptions.Value.ClaimsIdentity;
        if (!Guid.TryParse(principal.FindFirstValue(claimTypes.UserIdClaimType), out var userId))
        {
            return false;
        }

        var currentStamp = await cache.GetOrCreateAsync(CacheKey(userId), entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return db.Users.Where(user => user.Id == userId).Select(user => user.SecurityStamp).FirstOrDefaultAsync(cancellationToken);
        });

        return currentStamp is not null && currentStamp == principal.FindFirstValue(claimTypes.SecurityStampClaimType);
    }

    /// <summary>Called after renewing a user's security stamp, so this instance rejects their old tokens at once.</summary>
    public void Forget(Guid userId) => cache.Remove(CacheKey(userId));

    private static string CacheKey(Guid userId) => $"security-stamp:{userId}";
}
