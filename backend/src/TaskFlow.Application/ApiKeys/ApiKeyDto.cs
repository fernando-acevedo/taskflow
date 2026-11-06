using TaskFlow.Domain.ApiKeys;

namespace TaskFlow.Application.ApiKeys;

/// <param name="Prefix">The first characters of the secret, to recognize the key.</param>
/// <param name="IsActive">Not revoked and not expired.</param>
/// <param name="LastUsedAt">Approximate: updated at most every 5 minutes.</param>
public sealed record ApiKeyDto(
    Guid Id,
    string Name,
    string Prefix,
    ApiKeyScope Scope,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastUsedAt,
    bool IsActive);

/// <param name="Secret">The key itself. It is shown only in this response and cannot be read again.</param>
public sealed record CreatedApiKeyDto(
    Guid Id,
    string Name,
    string Prefix,
    ApiKeyScope Scope,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string Secret);
