using System.Net;
using System.Net.Http.Json;
using TaskFlow.Api.IntegrationTests.Infrastructure;

namespace TaskFlow.Api.IntegrationTests;

public sealed class HealthEndpointTests(SqlServerApiFactory factory) : IClassFixture<SqlServerApiFactory>
{
    [Fact]
    public async Task ReturnsHealthyWhenDatabaseIsReachable()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? string.Empty);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<HealthBody>(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.ShouldNotBeNull();
        body.Status.ShouldBe("Healthy");
        body.Checks.ShouldContain(c => c.Name == "database" && c.Status == "Healthy");
    }

    private sealed record HealthBody(string Status, IReadOnlyList<HealthCheck> Checks);

    private sealed record HealthCheck(string Name, string Status);
}
