namespace TaskFlow.Api.Identity;

public static class RateLimitPolicies
{
    /// <summary>Per-IP limit for anonymous endpoints that check credentials or tokens.</summary>
    public const string Auth = "auth";

    /// <summary>Stricter per-caller limit for dashboards and reports, which run heavy queries.</summary>
    public const string Report = "report";

    /// <summary>Strictest per-caller limit, for CSV exports.</summary>
    public const string Export = "export";
}
