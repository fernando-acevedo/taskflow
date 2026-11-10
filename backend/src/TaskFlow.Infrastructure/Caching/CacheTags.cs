namespace TaskFlow.Infrastructure.Caching;

/// <summary>Tags of the entries in the shared cache, so a change can drop everything it affects (ADR 0020).</summary>
public static class CacheTags
{
    /// <summary>The user's session: security stamp and whether API keys of that user may authenticate.</summary>
    public static string User(Guid userId) => $"user:{userId}";

    /// <summary>The user's memberships (access to each tenant and the default tenant).</summary>
    public static string Membership(Guid userId) => $"membership:{userId}";

    /// <summary>Everything cached about an API key.</summary>
    public static string ApiKey(Guid keyId) => $"apikey:{keyId}";
}
