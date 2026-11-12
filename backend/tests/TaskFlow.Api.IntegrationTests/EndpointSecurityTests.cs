using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Api.IntegrationTests.Infrastructure;

namespace TaskFlow.Api.IntegrationTests;

/// <summary>Guards against an endpoint that is public by accident. Runs without a database.</summary>
public sealed class EndpointSecurityTests(NoDatabaseApiFactory factory) : IClassFixture<NoDatabaseApiFactory>
{
    /// <summary>The endpoints meant to work without a token. Adding one here is a deliberate decision.</summary>
    private static readonly HashSet<string> PublicRoutes =
    [
        "health",
        "health/live",
        "health/ready",
        "api/system/info",
        "api/demo/status",
        "api/invitations/accept",
        "api/chatbot/message",
        "api/auth/register",
        "api/auth/login",
        "api/auth/refresh",
        "api/auth/confirmEmail",
        "api/auth/resendConfirmationEmail",
        "api/auth/forgotPassword",
        "api/auth/resetPassword",

        // The API reference is public in every environment and holds no data (ADR 0016).
        "openapi/{documentName}.json",
        "openapi/v1.json",
        "scalar/v1",
        "scalar/{documentName?}",
        "scalar/scalar.js",
        "scalar/scalar.aspnetcore.js",
        "scalar/favicon.svg",
    ];

    [Fact]
    public void EveryEndpointRequiresAuthorizationUnlessListedAsPublic()
    {
        var unprotected = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAuthorizeData>() is null
                || endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(endpoint => endpoint.RoutePattern.RawText!.TrimStart('/'))
            .Where(route => !PublicRoutes.Contains(route));

        unprotected.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("GET", "/api/users/me")]
    [InlineData("GET", "/api/admin/users")]
    [InlineData("POST", "/api/admin/users")]
    [InlineData("GET", "/api/tenants/mine")]
    [InlineData("GET", "/api/tenants")]
    [InlineData("POST", "/api/tenants")]
    [InlineData("GET", "/api/tenant/members")]
    [InlineData("POST", "/api/tenant/members")]
    [InlineData("GET", "/api/tenant/support-access")]
    [InlineData("GET", "/api/clients")]
    [InlineData("GET", "/api/clients/options")]
    [InlineData("POST", "/api/clients")]
    [InlineData("GET", "/api/projects")]
    [InlineData("POST", "/api/projects")]
    [InlineData("GET", "/api/projects/00000000-0000-0000-0000-000000000001")]
    [InlineData("GET", "/api/projects/00000000-0000-0000-0000-000000000001/tasks")]
    [InlineData("POST", "/api/projects/00000000-0000-0000-0000-000000000001/tasks")]
    [InlineData("DELETE", "/api/projects/00000000-0000-0000-0000-000000000001/tasks/00000000-0000-0000-0000-000000000002")]
    [InlineData("GET", "/api/projects/00000000-0000-0000-0000-000000000001/dashboard")]
    [InlineData("GET", "/api/dashboard")]
    public async Task AnonymousRequestIsUnauthorized(string method, string url)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(url, UriKind.Relative));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
