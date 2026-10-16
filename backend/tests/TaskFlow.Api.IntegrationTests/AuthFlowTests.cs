using System.Net;
using System.Net.Http.Json;
using TaskFlow.Api.IntegrationTests.Infrastructure;

namespace TaskFlow.Api.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class AuthFlowTests(SqlServerApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RegisteredUserCanLogInReadProfileAndRefresh()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? string.Empty);
        using var client = factory.CreateClient();
        var email = AuthApi.NewEmail();

        using var register = await client.RegisterAsync(email);
        register.StatusCode.ShouldBe(HttpStatusCode.OK);

        var tokens = await client.LoginForTokensAsync(email);
        tokens.TokenType.ShouldBe("Bearer");
        tokens.ExpiresIn.ShouldBe(3600);

        client.UseToken(tokens);
        var me = await client.GetFromJsonAsync<UserResponse>("/api/users/me", Ct);
        me.ShouldNotBeNull();
        me.Email.ShouldBe(email);
        me.DisplayName.ShouldBe(email.Split('@')[0]);
        me.Roles.ShouldBeEmpty();

        using var refresh = await client.RefreshAsync(tokens.RefreshToken);
        refresh.StatusCode.ShouldBe(HttpStatusCode.OK);
        var refreshed = await refresh.Content.ReadFromJsonAsync<TokenResponse>(Ct);
        refreshed!.AccessToken.ShouldNotBe(tokens.AccessToken);
    }

    [Fact]
    public async Task ProfileRequiresAuthentication()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? string.Empty);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/users/me", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RegisterRejectsDuplicateEmailAndWeakPasswordWithIdentityCodes()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? string.Empty);
        using var client = factory.CreateClient();
        var email = AuthApi.NewEmail();
        (await client.RegisterAsync(email)).EnsureSuccessStatusCode();

        using var duplicate = await client.RegisterAsync(email);
        using var weak = await client.RegisterAsync(AuthApi.NewEmail(), "short");

        duplicate.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await duplicate.Content.ReadFromJsonAsync<ProblemResponse>(Ct))!.Errors!.Keys.ShouldContain("DuplicateUserName");

        weak.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var weakErrors = (await weak.Content.ReadFromJsonAsync<ProblemResponse>(Ct))!.Errors!.Keys;
        weakErrors.ShouldContain("PasswordTooShort");
        weakErrors.ShouldContain("PasswordRequiresUpper");
    }

    [Fact]
    public async Task LoginFailsWithWrongPassword()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? string.Empty);
        var (_, email) = await factory.CreateUserClientAsync();
        using var client = factory.CreateClient();

        using var response = await client.LoginAsync(email, "Wrong-Passw0rd");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UserCanUpdateDisplayName()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? string.Empty);
        var (client, _) = await factory.CreateUserClientAsync();

        using var update = await client.PutAsJsonAsync("/api/users/me", new { displayName = "  Ada Lovelace " }, Ct);
        var me = await client.GetFromJsonAsync<UserResponse>("/api/users/me", Ct);

        update.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        me!.DisplayName.ShouldBe("Ada Lovelace");
        client.Dispose();
    }

    [Theory]
    [InlineData("en", "must not be empty")]
    [InlineData("es", "vacío")]
    public async Task InvalidDisplayNameReturnsLocalizedValidationProblem(string language, string expectedFragment)
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? string.Empty);
        var (client, _) = await factory.CreateUserClientAsync();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);

        using var response = await client.PutAsJsonAsync("/api/users/me", new { displayName = "" }, Ct);
        var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        problem!.Errors!["displayName"].ShouldContain(message => message.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase));
        client.Dispose();
    }
}
