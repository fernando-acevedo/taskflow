namespace TaskFlow.Api.Identity;

public static class RateLimitPolicies
{
    /// <summary>Per-IP limit for anonymous endpoints that check credentials or tokens.</summary>
    public const string Auth = "auth";
}
