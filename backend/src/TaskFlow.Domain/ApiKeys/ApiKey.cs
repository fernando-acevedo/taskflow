using System.Security.Cryptography;
using System.Text;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Tenancy;

namespace TaskFlow.Domain.ApiKeys;

/// <summary>What a key may do. Managing a project is never allowed to a key (ADR 0016).</summary>
public enum ApiKeyScope
{
    /// <summary>Read what the owner can read.</summary>
    Read,

    /// <summary>Read, and create, edit and move tasks where the owner is a member.</summary>
    Write,
}

/// <summary>The stored key and the secret that is shown to the person once.</summary>
public sealed record NewApiKey(ApiKey Key, string Secret);

/// <summary>
/// A credential for programs (ADR 0016). It belongs to one user in one tenant. Only the hash of the secret is stored:
/// the secret is random and long, so a fast hash is enough and lets the lookup be a plain indexed equality.
/// </summary>
public sealed class ApiKey : ITenantOwned
{
    public const string SecretPrefix = "tf_";
    public const int NameMaxLength = 100;

    /// <summary>Characters of the secret kept in clear (<c>tf_</c> and 8 more) so a person can tell keys apart.</summary>
    public const int DisplayPrefixLength = 11;

    public const int HashLength = 64;
    public const int MaxActivePerUser = 20;
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(90);
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(365);

    private const int SecretBytes = 32;

    private ApiKey(Guid id, Guid userId, string name, string prefix, string keyHash, ApiKeyScope scope, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        Id = id;
        UserId = userId;
        Name = name;
        Prefix = prefix;
        KeyHash = keyHash;
        Scope = scope;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Name { get; private set; }

    /// <summary>The first characters of the secret; not enough to use it.</summary>
    public string Prefix { get; private set; }

    /// <summary>Hex SHA-256 of the whole secret.</summary>
    public string KeyHash { get; private set; }

    public ApiKeyScope Scope { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public DateTimeOffset? LastUsedAt { get; private set; }

    /// <param name="expiresAt">Null means <see cref="DefaultLifetime"/> from now.</param>
    public static Result<NewApiKey> Create(Guid userId, string name, ApiKeyScope scope, DateTimeOffset? expiresAt, DateTimeOffset now)
    {
        var expiry = expiresAt ?? now + DefaultLifetime;
        if (expiry <= now)
        {
            return ApiKeyErrors.ExpiryInPast;
        }

        if (expiry > now + MaxLifetime)
        {
            return ApiKeyErrors.ExpiryTooFar;
        }

        var secret = SecretPrefix + Base64Url(RandomNumberGenerator.GetBytes(SecretBytes));
        var key = new ApiKey(
            Guid.CreateVersion7(now), userId, name, secret[..DisplayPrefixLength], HashSecret(secret), scope, now, expiry);
        return new NewApiKey(key, secret);
    }

    public static string HashSecret(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    }

    public bool IsUsable(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    /// <summary>Revoking twice keeps the first time.</summary>
    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;

    /// <summary>Records a use, unless one was recorded less than <paramref name="minInterval"/> ago. Returns whether it did.</summary>
    public bool RecordUse(DateTimeOffset now, TimeSpan minInterval)
    {
        if (LastUsedAt is { } last && now - last < minInterval)
        {
            return false;
        }

        LastUsedAt = now;
        return true;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public static class ApiKeyErrors
{
    public static readonly Error NotFound =
        Error.NotFound("ApiKeys.NotFound", "The API key was not found.");

    public static readonly Error ExpiryInPast =
        Error.Invalid("ApiKeys.ExpiryInPast", "The expiry date must be in the future.");

    public static readonly Error ExpiryTooFar =
        Error.Invalid("ApiKeys.ExpiryTooFar", "An API key can last at most one year.");

    public static readonly Error TooMany =
        Error.Conflict("ApiKeys.TooMany", "You have too many active API keys. Revoke one first.");

    public static readonly Error ScopeInsufficient =
        Error.Forbidden("ApiKeys.ScopeInsufficient", "This API key's scope doesn't allow this action.");

    public static readonly Error NotAllowedWithKey =
        Error.Forbidden("ApiKeys.NotAllowedWithKey", "API keys can't be used for this. Sign in as a person.");
}
