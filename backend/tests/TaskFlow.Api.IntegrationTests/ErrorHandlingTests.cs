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
    public async Task ProblemDetailsCarryTheTraceIdOfTheRequest()
    {
        using var failing = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton(ThrowingProxy.Create<IIdentityService>(new InvalidOperationException(SecretMessage)))));
        using var client = failing.CreateClient();
        const string traceId = "4bf92f3577b34da6a3ce929d0e0e4736";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/invitations/accept")
        {
            Content = JsonContent.Create(new { email = AuthApi.NewEmail(), token = "token", password = AuthApi.ValidPassword }),
        };
        request.Headers.Add("traceparent", $"00-{traceId}-00f067aa0ba902b7-01");

        // An unhandled exception (500) and a validation failure (400, written by MVC) both carry the id.
        using var serverError = await client.SendAsync(request, Ct);
        using var validation = await client.PostAsJsonAsync("/api/invitations/accept", new { }, Ct);

        serverError.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await TraceIdOfAsync(serverError)).ShouldBe(traceId);
        validation.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TraceIdOfAsync(validation))!.ShouldMatch("^[0-9a-f]{32}$");
    }

    private static async Task<string?> TraceIdOfAsync(HttpResponseMessage response)
    {
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return document.RootElement.TryGetProperty("traceId", out var value) ? value.GetString() : null;
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
