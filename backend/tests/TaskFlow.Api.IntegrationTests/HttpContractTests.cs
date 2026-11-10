using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using TaskFlow.Api.IntegrationTests.Infrastructure;

namespace TaskFlow.Api.IntegrationTests;

/// <summary>Security headers, compression and versioning (ADR 0022). These need no database.</summary>
public sealed class HttpContractTests(NoDatabaseApiFactory factory) : IClassFixture<NoDatabaseApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/api/system/info", 200)]
    [InlineData("/api/api-keys", 401)]
    [InlineData("/api/does-not-exist", 404)]
    public async Task ResponsesCarryTheSecurityHeaders(string url, int status)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri(url, UriKind.Relative), Ct);

        ((int)response.StatusCode).ShouldBe(status);
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.GetValues("X-Frame-Options").ShouldBe(["DENY"]);
        response.Headers.GetValues("Referrer-Policy").ShouldBe(["no-referrer"]);
        response.Headers.GetValues("Content-Security-Policy").ShouldBe(["default-src 'none'; frame-ancestors 'none'"]);
    }

    [Fact]
    public async Task TheApiReferenceGetsAPolicyThatLetsItLoad()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/scalar/1.0", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var policy = response.Headers.GetValues("Content-Security-Policy").ShouldHaveSingleItem();
        policy.ShouldContain("script-src 'self'");
        policy.ShouldContain("frame-ancestors 'none'");
    }

    [Theory]
    [InlineData("br")]
    [InlineData("gzip")]
    public async Task JsonIsCompressedWhenTheClientAsksForIt(string encoding)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/system/info");
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue(encoding));

        using var response = await client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentEncoding.ShouldBe([encoding]);
        response.Headers.Vary.ShouldContain("Accept-Encoding");
    }

    [Fact]
    public async Task ResponsesAreNotCompressedWithoutAcceptEncoding()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/system/info", UriKind.Relative), Ct);

        response.Content.Headers.ContentEncoding.ShouldBeEmpty();
    }

    [Fact]
    public async Task ResponsesReportTheSupportedVersionsAndAssumeV1()
    {
        using var client = factory.CreateClient();

        using var unversioned = await client.GetAsync(new Uri("/api/system/info", UriKind.Relative), Ct);
        using var byQuery = await client.GetAsync(new Uri("/api/system/info?api-version=1.0", UriKind.Relative), Ct);
        using var byHeader = await Send(client, "1.0");

        foreach (var response in new[] { unversioned, byQuery, byHeader })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Headers.GetValues("api-supported-versions").ShouldBe(["1.0"]);
        }
    }

    [Fact]
    public async Task AnUnsupportedVersionIsABadRequest()
    {
        using var client = factory.CreateClient();

        using var byQuery = await client.GetAsync(new Uri("/api/system/info?api-version=2.0", UriKind.Relative), Ct);
        using var byHeader = await Send(client, "2.0");

        foreach (var response in new[] { byQuery, byHeader })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
            (await response.ReadAsync<ProblemResponse>()).Code.ShouldBe("UnsupportedApiVersion");
        }
    }

    [Fact]
    public async Task TheOpenApiDocumentIsServedPerVersionAndTheOldAddressRedirects()
    {
        using var client = factory.CreateClient();

        using var document = await client.GetAsync(new Uri("/openapi/1.0.json", UriKind.Relative), Ct);
        using var old = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), Ct);

        document.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await document.Content.ReadAsStringAsync(Ct)).ShouldContain("/api/projects/{projectId}");
        old.StatusCode.ShouldBe(HttpStatusCode.OK);
        old.RequestMessage!.RequestUri!.AbsolutePath.ShouldBe("/openapi/1.0.json");
    }

    [Fact]
    public async Task IdentityEndpointsIgnoreTheVersion()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new { }) };
        request.Headers.Add("api-version", "2.0");

        using var response = await client.SendAsync(request, Ct);

        // Reaches Identity's own handler (an empty body is a validation problem), not the versioning rejection.
        response.StatusCode.ShouldNotBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldNotContain("UnsupportedApiVersion");
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, string version)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/system/info");
        request.Headers.Add("api-version", version);
        return client.SendAsync(request, Ct);
    }
}

/// <summary>Per-user rate limits (ADR 0022), the version/credential gap and the 429 contract.</summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class RateLimitAndSessionTests(SqlServerApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task APersonOverTheirBudgetGets429WithRetryAfterAndALocalizedProblem()
    {
        var (userClient, email) = await factory.CreateUserClientAsync();
        userClient.Dispose();
        using var limited = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:User:TokenLimit", "3");
            builder.UseSetting("RateLimiting:User:TokensPerPeriod", "1");
            builder.UseSetting("RateLimiting:User:ReplenishmentSeconds", "3600");
        });
        using var client = limited.CreateClient();
        client.UseToken(await client.LoginForTokensAsync(email));
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es");

        var statuses = new List<HttpStatusCode>();
        HttpResponseMessage? rejected = null;
        for (var call = 0; call < 5; call++)
        {
            var response = await client.GetAsync(new Uri("/api/clients/options", UriKind.Relative), Ct);
            statuses.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rejected ??= response;
            }
            else
            {
                response.Dispose();
            }
        }

        using var _ = rejected;
        statuses.Take(3).ShouldAllBe(status => status == HttpStatusCode.OK);
        statuses.Skip(3).ShouldAllBe(status => status == HttpStatusCode.TooManyRequests);
        var retryAfter = int.Parse(rejected!.Headers.GetValues("Retry-After").Single(), System.Globalization.CultureInfo.InvariantCulture);
        retryAfter.ShouldBeGreaterThan(0);
        rejected.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var problem = await rejected.ReadAsync<ProblemResponse>();
        (problem.Status, problem.Code).ShouldBe((429, "RateLimit.Exceeded"));
        problem.Title.ShouldBe("Demasiadas solicitudes. Espera un momento e inténtalo de nuevo.");
    }

    [Fact]
    public async Task ExportsHaveTheirOwnStricterBudget()
    {
        var (ownerClient, email) = await factory.CreateUserClientAsync();
        var project = await factory.CreateProjectAsync(ownerClient);
        ownerClient.Dispose();
        using var limited = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:Export:TokenLimit", "1");
            builder.UseSetting("RateLimiting:Export:TokensPerPeriod", "1");
            builder.UseSetting("RateLimiting:Export:ReplenishmentSeconds", "3600");
        });
        using var client = limited.CreateClient();
        client.UseToken(await client.LoginForTokensAsync(email));
        var csv = new Uri($"/api/projects/{project.Id}/time-summary.csv?from={factory.Today:yyyy-MM-dd}&to={factory.Today:yyyy-MM-dd}", UriKind.Relative);

        using var first = await client.GetAsync(csv, Ct);
        using var second = await client.GetAsync(csv, Ct);
        using var other = await client.GetAsync(new Uri($"/api/projects/{project.Id}", UriKind.Relative), Ct);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        second.Headers.Contains("Retry-After").ShouldBeTrue();
        other.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ChangingThePasswordThroughManageInfoEndsTheOldSessionAtOnce()
    {
        var (userClient, _) = await factory.CreateUserClientAsync();
        using var client = userClient;
        using (var before = await client.GetAsync(new Uri("/api/auth/manage/info", UriKind.Relative), Ct))
        {
            // Warms the cached security stamp: without the invalidation this token would live on for its 30 seconds.
            before.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var change = await client.PostAsJsonAsync(
            "/api/auth/manage/info", new { oldPassword = AuthApi.ValidPassword, newPassword = "N3w-Passw0rd!" }, Ct);
        using var after = await client.GetAsync(new Uri("/api/auth/manage/info", UriKind.Relative), Ct);

        change.StatusCode.ShouldBe(HttpStatusCode.OK);
        after.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
