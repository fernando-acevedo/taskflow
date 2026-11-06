using System.Net;
using System.Net.Http.Json;
using TaskFlow.Api.IntegrationTests.Infrastructure;

namespace TaskFlow.Api.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class ThemePreferenceTests(SqlServerApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record ThemeResponse(string ThemePreference);

    [Fact]
    public async Task ThemeDefaultsToSystemAndCanBeChangedAndReadBack()
    {
        var (userClient, _) = await factory.CreateUserClientAsync();
        using var client = userClient;
        (await client.GetJsonAsync<ThemeResponse>("/api/users/me")).ThemePreference.ShouldBe("System");

        using var update = await client.PutAsJsonAsync("/api/users/me/preferences", new { themePreference = "Dark" }, Ct);
        update.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await client.GetJsonAsync<ThemeResponse>("/api/users/me")).ThemePreference.ShouldBe("Dark");
    }

    [Theory]
    [InlineData("Neon")]
    [InlineData("")]
    [InlineData(99)]
    public async Task InvalidThemeIsRejected(object value)
    {
        var (userClient, _) = await factory.CreateUserClientAsync();
        using var client = userClient;

        using var response = await client.PutAsJsonAsync("/api/users/me/preferences", new { themePreference = value }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.GetJsonAsync<ThemeResponse>("/api/users/me")).ThemePreference.ShouldBe("System");
    }

    [Fact]
    public async Task PreferencesRequireAuthentication()
    {
        using var client = factory.CreateClient();

        using var response = await client.PutAsJsonAsync("/api/users/me/preferences", new { themePreference = "Light" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
