using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Api.IntegrationTests.Infrastructure;
using TaskFlow.Application.Users;

namespace TaskFlow.Api.IntegrationTests;

/// <summary>The cross-cutting HTTP behavior: unhandled exceptions and the auth rate limit. Runs without a database.</summary>
public sealed class ErrorHandlingTests(NoDatabaseApiFactory factory) : IClassFixture<NoDatabaseApiFactory>
{
    private const string SecretMessage = "connection string with a password";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UnhandledExceptionIsAProblemDetailsWithoutInternals()
    {
        using var failing = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton(ThrowingProxy.Create<IIdentityService>(new InvalidOperationException(SecretMessage)))));
        using var client = failing.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/invitations/accept", new { email = AuthApi.NewEmail(), token = "token", password = AuthApi.ValidPassword }, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var problem = await response.ReadAsync<ProblemResponse>();
        problem.Status.ShouldBe(500);
        problem.Title.ShouldBe("An unexpected error occurred.");
        body.ShouldNotContain(SecretMessage);
        body.ShouldNotContain(nameof(InvalidOperationException));
    }

    [Fact]
    public async Task AuthEndpointsAreRateLimited()
    {
        using var limited = factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:Auth:PermitLimit", "2"));
        using var client = limited.CreateClient();

        // An empty body fails validation before anything touches the database, so only the limiter decides.
        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var response = await client.PostAsJsonAsync("/api/invitations/accept", new { }, Ct);
            statuses.Add(response.StatusCode);
        }

        statuses.ShouldBe([HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.TooManyRequests]);
    }

    /// <summary>An implementation of any interface whose every member throws.</summary>
#pragma warning disable CA1852 // DispatchProxy.Create refuses sealed proxy types.
    private class ThrowingProxy : DispatchProxy
#pragma warning restore CA1852
    {
        private Exception _exception = new InvalidOperationException();

        public static T Create<T>(Exception exception)
            where T : class
        {
            var proxy = DispatchProxy.Create<T, ThrowingProxy>();
            ((ThrowingProxy)(object)proxy)._exception = exception;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw _exception;
    }
}
