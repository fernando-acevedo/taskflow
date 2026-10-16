using System.Net;
using System.Net.Http.Json;
using TaskFlow.Api.Controllers;
using TaskFlow.Api.IntegrationTests.Infrastructure;

namespace TaskFlow.Api.IntegrationTests;

public sealed class SystemInfoEndpointTests(NoDatabaseApiFactory factory) : IClassFixture<NoDatabaseApiFactory>
{
    private static readonly Uri InfoUri = new("/api/system/info", UriKind.Relative);

    [Theory]
    [InlineData("en-US", "en", "Welcome to the TaskFlow API.")]
    [InlineData("es-AR", "es", "Bienvenido a la API de TaskFlow.")]
    [InlineData("fr-FR", "en", "Welcome to the TaskFlow API.")]
    public async Task ReturnsMessageInRequestedLanguage(string acceptLanguage, string expectedCulture, string expectedMessage)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, InfoUri);
        request.Headers.AcceptLanguage.ParseAdd(acceptLanguage);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<SystemInfoResponse>(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.ShouldNotBeNull();
        body.Culture.ShouldBe(expectedCulture);
        body.Message.ShouldBe(expectedMessage);
    }
}
