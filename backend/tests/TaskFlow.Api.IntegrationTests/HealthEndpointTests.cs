using System.Net;
using TaskFlow.Api.IntegrationTests.Infrastructure;

namespace TaskFlow.Api.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class HealthEndpointTests(SqlServerApiFactory factory)
{
    [Fact]
    public async Task ReturnsHealthyWhenDatabaseIsReachable()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);
        var body = await response.ReadAsync<HealthBody>();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.Status.ShouldBe("Healthy");
        body.Checks.ShouldContain(c => c.Name == "database" && c.Status == "Healthy");
    }

    private sealed record HealthBody(string Status, IReadOnlyList<HealthCheck> Checks);

    private sealed record HealthCheck(string Name, string Status);
}
