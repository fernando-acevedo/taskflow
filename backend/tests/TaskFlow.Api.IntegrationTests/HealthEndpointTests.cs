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

    [Theory]
    [InlineData("/health/ready")]
    [InlineData("/health/live")]
    public async Task LiveAndReadyAnswerOk(string path)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task LiveDoesNotCheckTheDatabase()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);
        var body = await response.ReadAsync<HealthBody>();

        body.Checks.ShouldBeEmpty();
    }

    private sealed record HealthBody(string Status, IReadOnlyList<HealthCheck> Checks);

    private sealed record HealthCheck(string Name, string Status);
}
