using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using TaskFlow.Api.IntegrationTests.Infrastructure;

namespace TaskFlow.Api.IntegrationTests;

/// <summary>Per-user rate limits ignore the X-Tenant header and answer 429 with Retry-After (ADR 0022).</summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class RateLimitTests(SqlServerApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(WebApplicationFactory<Program> Host, HttpClient Client)> LimitedAsync(params (string Key, string Value)[] settings)
    {
        var (user, email) = await factory.CreateUserClientAsync();
        user.Dispose();
        var host = factory.WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        });
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await client.LoginForTokensAsync(email)).AccessToken);
        return (host, client);
    }

    private static HttpRequestMessage Get(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("X-Tenant", Guid.NewGuid().ToString());
        return request;
    }

    [Fact]
    public async Task DifferentTenantHeadersShareTheSameUserBucket()
    {
        var (host, client) = await LimitedAsync(
            ("RateLimiting:User:TokenLimit", "3"), ("RateLimiting:User:TokensPerPeriod", "1"), ("RateLimiting:User:ReplenishmentSeconds", "3600"));
        await using var _ = host;
        using var __ = client;

        for (var i = 0; i < 3; i++)
        {
            using var allowed = await client.SendAsync(Get("/api/system/info"), Ct);
            allowed.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
        }

        using var limited = await client.SendAsync(Get("/api/system/info"), Ct);

        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter.ShouldNotBeNull();
        (await limited.ReadAsync<ProblemResponse>()).Code.ShouldBe("RateLimit.Exceeded");
    }

    [Theory]
    [InlineData("/api/dashboard", "Report")]
    [InlineData("/api/projects/00000000-0000-0000-0000-000000000001/time-summary.csv?from=2026-01-01&to=2026-01-02", "Export")]
    public async Task ReportAndExportPoliciesAnswer429AcrossTenantHeaders(string url, string policy)
    {
        var (host, client) = await LimitedAsync(
            ($"RateLimiting:{policy}:TokenLimit", "1"), ($"RateLimiting:{policy}:TokensPerPeriod", "1"), ($"RateLimiting:{policy}:ReplenishmentSeconds", "3600"));
        await using var _ = host;
        using var __ = client;

        using var first = await client.SendAsync(Get(url), Ct);
        using var second = await client.SendAsync(Get(url), Ct);

        first.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
        second.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        second.Headers.RetryAfter.ShouldNotBeNull();
    }
}
