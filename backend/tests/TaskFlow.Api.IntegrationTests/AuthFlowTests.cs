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
        using var client = factory.CreateClient();
        var email = AuthApi.NewEmail();

        using var register = await client.RegisterAsync(email);
        register.StatusCode.ShouldBe(HttpStatusCode.OK);

        var tokens = await client.LoginForTokensAsync(email);
        tokens.TokenType.ShouldBe("Bearer");
        tokens.ExpiresIn.ShouldBe(3600);

        client.UseToken(tokens);
        var me = await client.GetJsonAsync<UserResponse>("/api/users/me");
        me.Email.ShouldBe(email);
        me.DisplayName.ShouldBe(email.Split('@')[0]);
        me.Roles.ShouldBeEmpty();

        using var refresh = await client.RefreshAsync(tokens.RefreshToken);
        refresh.StatusCode.ShouldBe(HttpStatusCode.OK);
        var refreshed = await refresh.ReadAsync<TokenResponse>();
        refreshed.AccessToken.ShouldNotBe(tokens.AccessToken);
    }

    [Fact]
    public async Task ProfileRequiresAuthentication()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/users/me", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RegisterRejectsDuplicateEmailAndWeakPasswordWithIdentityCodes()
    {
        using var client = factory.CreateClient();
        var email = AuthApi.NewEmail();
        (await client.RegisterAsync(email)).EnsureSuccessStatusCode();

        using var duplicate = await client.RegisterAsync(email);
        using var weak = await client.RegisterAsync(AuthApi.NewEmail(), "short");

        duplicate.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await duplicate.ReadAsync<ProblemResponse>()).Errors!.Keys.ShouldContain("DuplicateUserName");

        weak.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var weakErrors = (await weak.ReadAsync<ProblemResponse>()).Errors!.Keys;
        weakErrors.ShouldContain("PasswordTooShort");
        weakErrors.ShouldContain("PasswordRequiresUpper");
    }

    [Fact]
    public async Task LoginFailsWithWrongPassword()
    {
        var (userClient, email) = await factory.CreateUserClientAsync();
        userClient.Dispose();
        using var client = factory.CreateClient();

        using var response = await client.LoginAsync(email, "Wrong-Passw0rd");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UserCanUpdateDisplayName()
    {
        var (userClient, _) = await factory.CreateUserClientAsync();
        using var client = userClient;

        using var update = await client.PutAsJsonAsync("/api/users/me", new { displayName = "  Ada Lovelace " }, Ct);
        update.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var me = await client.GetJsonAsync<UserResponse>("/api/users/me");
        me.DisplayName.ShouldBe("Ada Lovelace");
    }

    [Theory]
    [InlineData("en", "must not be empty")]
    [InlineData("es", "vacío")]
    public async Task InvalidDisplayNameReturnsLocalizedValidationProblem(string language, string expectedFragment)
    {
        var (userClient, _) = await factory.CreateUserClientAsync();
        using var client = userClient;
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);

        using var response = await client.PutAsJsonAsync("/api/users/me", new { displayName = "" }, Ct);
        var problem = await response.ReadAsync<ProblemResponse>();

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        problem.Errors!["displayName"].ShouldContain(message => message.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase));
    }
}
