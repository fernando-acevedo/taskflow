namespace TaskFlow.Api.Security;

/// <summary>Checks the configured CORS origins at startup: the policy allows credentials (the SignalR client sends them), so the list must be explicit.</summary>
internal static class CorsOrigins
{
    public static string[] Validate(string[]? origins)
    {
        if (origins is null || origins.Length == 0)
        {
            throw new InvalidOperationException(
                "Cors:AllowedOrigins is empty: list the frontend origin(s) (for example Cors__AllowedOrigins__0=https://app.example.com). The CORS policy allows credentials, so it needs explicit origins.");
        }

        foreach (var origin in origins)
        {
            if (string.IsNullOrWhiteSpace(origin))
            {
                throw new InvalidOperationException("Cors:AllowedOrigins contains an empty origin.");
            }

            if (origin.Contains('*', StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Cors:AllowedOrigins contains '{origin}': a wildcard cannot be combined with AllowCredentials. List each origin explicitly.");
            }
        }

        return origins;
    }
}
