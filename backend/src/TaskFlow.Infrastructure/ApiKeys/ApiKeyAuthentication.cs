using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskFlow.Application.Demo;
using TaskFlow.Domain.ApiKeys;
using TaskFlow.Infrastructure.Caching;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Infrastructure.ApiKeys;

/// <summary>Names of the authentication schemes and claims of API keys (ADR 0016).</summary>
public static class ApiKeyDefaults
{
    public const string Scheme = "ApiKey";

    /// <summary>The default scheme: sends <c>tf_</c> bearer tokens to <see cref="Scheme"/> and everything else to Identity.</summary>
    public const string SelectorScheme = "TaskFlow";

    public const string KeyIdClaim = "tf:api_key";
    public const string TenantClaim = "tf:tenant";
    public const string ScopeClaim = "tf:scope";

    /// <summary>How long a check that the key's owner is active is reused (same as the security stamp's).</summary>
    public static readonly TimeSpan OwnerCacheDuration = TimeSpan.FromSeconds(30);

    public static string OwnerCacheKey(Guid userId) => $"apikey-owner:{userId}";

    /// <summary>How long an unknown secret is remembered (by hash, never the secret) so repeats don't query the database.</summary>
    public static readonly TimeSpan UnknownCacheDuration = TimeSpan.FromSeconds(5);

    public static string UnknownCacheKey(string hash) => $"apikey-unknown:{hash}";

    /// <summary><c>LastUsedAt</c> is written at most this often per key.</summary>
    public static readonly TimeSpan LastUsedInterval = TimeSpan.FromMinutes(5);

    /// <summary>The secret in an <c>Authorization: Bearer tf_...</c> header, or <c>null</c> when it is not an API key.</summary>
    public static string? SecretFrom(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        const string bearer = "Bearer ";
        var header = request.Headers.Authorization.ToString();
        return header.StartsWith(bearer, StringComparison.OrdinalIgnoreCase)
            && header.AsSpan(bearer.Length).StartsWith(ApiKey.SecretPrefix, StringComparison.Ordinal)
                ? header[bearer.Length..].Trim()
                : null;
    }
}

/// <summary>
/// Authenticates <c>Authorization: Bearer tf_...</c>. The key is found by the hash of the secret, which happens before
/// the request has a tenant, so this is the only lookup that ignores the tenant filter (the key itself says which
/// tenant the request is in). Found keys are not cached (a revoked key stops working at once); unknown secrets are, for
/// seconds and per instance. Whether the owner is active is cached under the user's tag (see <see cref="Identity.SessionValidator.ForgetAsync"/>).
/// </summary>
internal sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    AppDbContext db,
    HybridCache cache,
    IMemoryCache unknownSecrets,
    IDemoMode demo,
    TimeProvider time)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var secret = ApiKeyDefaults.SecretFrom(Request);
        if (secret is null)
        {
            return AuthenticateResult.NoResult();
        }

        if (demo.IsEnabled)
        {
            return AuthenticateResult.Fail("API keys are disabled in the public demo.");
        }

        var cancellationToken = Context.RequestAborted;
        var hash = ApiKey.HashSecret(secret);
        if (unknownSecrets.TryGetValue(ApiKeyDefaults.UnknownCacheKey(hash), out _))
        {
            return AuthenticateResult.Fail("Invalid API key.");
        }

        // Plain equality in SQL is enough: the hash is SHA-256 of 256 random bits, so nothing can be learned from timing.
        var found = await db.ApiKeys.IgnoreQueryFilters().AsNoTracking()
            .Where(key => key.KeyHash == hash)
            .Select(key => new { Key = key, TenantId = EF.Property<Guid>(key, AppDbContext.TenantIdProperty) })
            .FirstOrDefaultAsync(cancellationToken);

        var now = time.GetUtcNow();
        if (found is null)
        {
            unknownSecrets.Set(ApiKeyDefaults.UnknownCacheKey(hash), true, ApiKeyDefaults.UnknownCacheDuration);
            return AuthenticateResult.Fail("Invalid API key.");
        }

        if (!found.Key.IsUsable(now))
        {
            return AuthenticateResult.Fail("Invalid API key.");
        }

        var key = found.Key;
        var ownerActive = await cache.GetOrCreateAsync(
            ApiKeyDefaults.OwnerCacheKey(key.UserId),
            token => new ValueTask<bool>(db.Users.Where(user => user.Id == key.UserId).Select(user => user.IsActive).FirstOrDefaultAsync(token)),
            new HybridCacheEntryOptions { Expiration = ApiKeyDefaults.OwnerCacheDuration, LocalCacheExpiration = ApiKeyDefaults.OwnerCacheDuration },
            [CacheTags.User(key.UserId), CacheTags.ApiKey(key.Id)],
            cancellationToken);
        if (!ownerActive)
        {
            return AuthenticateResult.Fail("The API key's owner is not active.");
        }

        if (key.RecordUse(now, ApiKeyDefaults.LastUsedInterval))
        {
            await db.ApiKeys.IgnoreQueryFilters()
                .Where(stored => stored.Id == key.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(stored => stored.LastUsedAt, key.LastUsedAt), cancellationToken);
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, key.UserId.ToString()),
                new Claim(ApiKeyDefaults.KeyIdClaim, key.Id.ToString()),
                new Claim(ApiKeyDefaults.TenantClaim, found.TenantId.ToString()),
                new Claim(ApiKeyDefaults.ScopeClaim, key.Scope.ToString()),
            ],
            Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}
