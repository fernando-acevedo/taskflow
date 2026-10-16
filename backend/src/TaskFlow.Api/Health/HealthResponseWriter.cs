using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TaskFlow.Api.Health;

/// <summary>Writes the health report as compact JSON the frontend can read.</summary>
internal static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        var response = new HealthResponse(
            report.Status.ToString(),
            report.TotalDuration.TotalMilliseconds,
            [.. report.Entries.Select(e => new HealthCheckEntry(e.Key, e.Value.Status.ToString(), e.Value.Duration.TotalMilliseconds))]);

        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(response, JsonOptions));
    }

    private sealed record HealthResponse(string Status, double TotalDurationMs, IReadOnlyList<HealthCheckEntry> Checks);

    private sealed record HealthCheckEntry(string Name, string Status, double DurationMs);
}
