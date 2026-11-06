using System.Net;
using TaskFlow.Api.IntegrationTests.Infrastructure;

namespace TaskFlow.Api.IntegrationTests;

/// <summary>API keys at the HTTP edge, without a database (ADR 0016). The flows that need data live with the other API tests.</summary>
public sealed class ApiKeySecurityTests(NoDatabaseApiFactory factory) : IClassFixture<NoDatabaseApiFactory>
{
    [Fact]
    public async Task TheApiReferenceIsPublicAndDeclaresTheBearerScheme()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        json.ShouldContain("/api/api-keys");
        json.ShouldContain("/api/projects/{projectId}/tasks/sync");
        json.ShouldContain("/api/projects/{projectId}/tasks/by-key/{externalKey}");
        json.ShouldContain("\"bearer\"");
    }

    [Fact]
    public async Task ManagingKeysRequiresASignedInPerson()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/api-keys", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
