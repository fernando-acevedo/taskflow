using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using TaskFlow.Api.Security;
using TaskFlow.Api.Telemetry;

namespace TaskFlow.Api.IntegrationTests;

/// <summary>The hub's access_token never reaches a trace (ADR 0023), and the CORS origins are checked at startup.</summary>
public sealed class TelemetryRedactionTests
{
    private static Activity Tagged(string? query, string? target = null, string? full = null)
    {
        var activity = new Activity("test");
        activity.SetTag("url.query", query);
        activity.SetTag("http.target", target);
        activity.SetTag("url.full", full);
        return activity;
    }

    [Fact]
    public void HubQueriesAreRedactedInEveryUrlTag()
    {
        using var activity = Tagged("id=abc&access_token=SECRET", "/hubs/projects?id=abc&access_token=SECRET", "https://api.test/hubs/projects?access_token=SECRET");

        TelemetryRedaction.RedactHubQuery(activity, new PathString("/hubs/projects"), hasQuery: true);

        activity.GetTagItem("url.query").ShouldBe(TelemetryRedaction.Redacted);
        activity.GetTagItem("http.target").ShouldBe("/hubs/projects?REDACTED");
        activity.GetTagItem("url.full").ShouldBe("https://api.test/hubs/projects?REDACTED");
        activity.TagObjects.ShouldAllBe(tag => tag.Value == null || !tag.Value.ToString()!.Contains("SECRET", StringComparison.Ordinal));
    }

    [Fact]
    public void OtherPathsKeepTheirQuery()
    {
        using var activity = Tagged("page=2", "/api/projects?page=2");

        TelemetryRedaction.RedactHubQuery(activity, new PathString("/api/projects"), hasQuery: true);

        activity.GetTagItem("url.query").ShouldBe("page=2");
        activity.GetTagItem("http.target").ShouldBe("/api/projects?page=2");
    }

    [Fact]
    public void AHubRequestWithoutQueryIsLeftAlone()
    {
        using var activity = Tagged(null);

        TelemetryRedaction.RedactHubQuery(activity, new PathString("/hubs/projects"), hasQuery: false);

        activity.GetTagItem("url.query").ShouldBeNull();
    }

    [Fact]
    public void BadCorsOriginsFailClearly()
    {
        string[]?[] bad = [null, [], ["https://app.example.com", "*"], ["https://*.example.com"], [" "]];
        foreach (var origins in bad)
        {
            Should.Throw<InvalidOperationException>(() => CorsOrigins.Validate(origins)).Message.ShouldContain("Cors:AllowedOrigins");
        }
    }

    [Fact]
    public void ExplicitCorsOriginsPass() =>
        CorsOrigins.Validate(["https://app.example.com"]).ShouldBe(["https://app.example.com"]);
}
