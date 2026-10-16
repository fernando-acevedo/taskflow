using System.Net;
using System.Net.Http.Json;
using TaskFlow.Api.IntegrationTests.Infrastructure;

namespace TaskFlow.Api.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class AdminUsersTests(SqlServerApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StandardUserCannotUseAdminEndpoints()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? string.Empty);
        var (client, _) = await factory.CreateUserClientAsync();

        using var response = await client.GetAsync(new Uri("/api/admin/users", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        client.Dispose();
    }

    [Fact]
    public async Task SeededAdminCanSearchUsers()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? string.Empty);
        var (userClient, email) = await factory.CreateUserClientAsync();
        userClient.Dispose();
        using var admin = await factory.CreateAdminClientAsync();

        var page = await admin.GetFromJsonAsync<PagedResponse<UserResponse>>($"/api/admin/users?search={email}", Ct);

        page!.TotalCount.ShouldBe(1);
        page.Items.Single().Email.ShouldBe(email);
        page.Items.Single().IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task DeactivatedUserCannotLogInOrRefreshUntilReactivated()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? string.Empty);
        using var admin = await factory.CreateAdminClientAsync();
        using var client = factory.CreateClient();
        var email = AuthApi.NewEmail();
        (await client.RegisterAsync(email)).EnsureSuccessStatusCode();
        var tokens = await client.LoginForTokensAsync(email);
        var userId = await FindUserIdAsync(admin, email);

        using var deactivate = await admin.PostAsync(new Uri($"/api/admin/users/{userId}/deactivate", UriKind.Relative), null, Ct);
        using var loginWhileInactive = await client.LoginAsync(email);
        using var refreshWhileInactive = await client.RefreshAsync(tokens.RefreshToken);

        deactivate.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        loginWhileInactive.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await loginWhileInactive.Content.ReadFromJsonAsync<ProblemResponse>(Ct))!.Detail.ShouldBe("LockedOut");
        refreshWhileInactive.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var activate = await admin.PostAsync(new Uri($"/api/admin/users/{userId}/activate", UriKind.Relative), null, Ct);
        using var loginAgain = await client.LoginAsync(email);

        activate.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        loginAgain.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PromotedUserGetsAdminRoleOnNextLogin()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? string.Empty);
        using var admin = await factory.CreateAdminClientAsync();
        using var client = factory.CreateClient();
        var email = AuthApi.NewEmail();
        (await client.RegisterAsync(email)).EnsureSuccessStatusCode();
        var userId = await FindUserIdAsync(admin, email);

        using var promote = await admin.PutAsync(new Uri($"/api/admin/users/{userId}/admin", UriKind.Relative), null, Ct);
        client.UseToken(await client.LoginForTokensAsync(email));
        using var adminEndpoint = await client.GetAsync(new Uri("/api/admin/users", UriKind.Relative), Ct);

        promote.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<UserResponse>("/api/users/me", Ct))!.Roles.ShouldBe(["Admin"]);
        adminEndpoint.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("en", "You can't deactivate or reactivate your own account.")]
    [InlineData("es", "No puedes desactivar ni reactivar tu propia cuenta.")]
    public async Task AdminCannotDeactivateThemselves(string language, string expectedTitle)
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? string.Empty);
        using var admin = await factory.CreateAdminClientAsync();
        admin.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);
        var adminId = (await admin.GetFromJsonAsync<UserResponse>("/api/users/me", Ct))!.Id;

        using var response = await admin.PostAsync(new Uri($"/api/admin/users/{adminId}/deactivate", UriKind.Relative), null, Ct);
        var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        problem!.Code.ShouldBe("Users.CannotChangeOwnStatus");
        problem.Title.ShouldBe(expectedTitle);
    }

    [Fact]
    public async Task UnknownUserReturnsNotFound()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? string.Empty);
        using var admin = await factory.CreateAdminClientAsync();

        using var response = await admin.PostAsync(new Uri($"/api/admin/users/{Guid.NewGuid()}/deactivate", UriKind.Relative), null, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<ProblemResponse>(Ct))!.Code.ShouldBe("Users.NotFound");
    }

    private static async Task<Guid> FindUserIdAsync(HttpClient admin, string email)
    {
        var page = await admin.GetFromJsonAsync<PagedResponse<UserResponse>>($"/api/admin/users?search={email}", Ct);
        return page!.Items.Single().Id;
    }
}
