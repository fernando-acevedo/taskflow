using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using TaskFlow.Api.IntegrationTests.Infrastructure;

namespace TaskFlow.Api.IntegrationTests;

public sealed class SelfRegistrationTests(NoDatabaseApiFactory factory) : IClassFixture<NoDatabaseApiFactory>
{
    [Fact]
    public async Task RegisterIsNotFoundWhenSelfRegistrationIsOff()
    {
        using var closed = factory.WithWebHostBuilder(builder => builder.UseSetting("Auth:AllowSelfRegistration", "false"));
        using var client = closed.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { email = AuthApi.NewEmail(), password = AuthApi.ValidPassword },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
