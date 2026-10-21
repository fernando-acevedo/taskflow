using System.Net;
using TaskFlow.Api.IntegrationTests.Infrastructure;

namespace TaskFlow.Api.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class AdminUsersTests(SqlServerApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StandardUserCannotUseAdminEndpoints()
    {
        var (userClient, _) = await factory.CreateUserClientAsync();
        using var user = userClient;

        using var list = await user.GetAsync(new Uri("/api/admin/users", UriKind.Relative), Ct);
        using var invite = await user.InviteUserAsync(AuthApi.NewEmail());
        using var deactivate = await user.DeactivateUserAsync(await user.GetMyIdAsync());

        list.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        invite.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        deactivate.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SeededAdminCanSearchUsers()
    {
        var (userClient, email) = await factory.CreateUserClientAsync();
        userClient.Dispose();
        using var admin = await factory.CreateAdminClientAsync();

        var page = await admin.GetJsonAsync<PagedResponse<UserResponse>>($"/api/admin/users?search={email}");

        page.TotalCount.ShouldBe(1);
        var user = page.Items.ShouldHaveSingleItem();
        user.Email.ShouldBe(email);
        user.IsActive.ShouldBeTrue();
        user.IsInvitationPending.ShouldBeFalse();
        user.CreatedAt.ShouldBe(factory.Time.GetUtcNow());
    }

    [Fact]
    public async Task InvitedUserChoosesAPasswordAndLogsIn()
    {
        using var admin = await factory.CreateAdminClientAsync();
        using var client = factory.CreateClient();
        var email = AuthApi.NewEmail();

        using var response = await admin.InviteUserAsync(email, isAdmin: true);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.ReadAsync<CreatedUserResponse>();
        var created = body.User;
        response.Headers.Location!.AbsolutePath.ShouldBe($"/api/admin/users/{created.Id}");
        body.InvitationSent.ShouldBeTrue();
        created.DisplayName.ShouldBe("New Person");
        created.Roles.ShouldBe(["Admin"]);
        created.IsInvitationPending.ShouldBeTrue();

        using var loginBeforeAccepting = await client.LoginAsync(email);
        loginBeforeAccepting.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var accept = await client.AcceptInvitationAsync(email, factory.Emails.InvitationTokenFor(email), AuthApi.ValidPassword);
        accept.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        client.UseToken(await client.LoginForTokensAsync(email));
        var me = await client.GetJsonAsync<UserResponse>("/api/users/me");
        me.Id.ShouldBe(created.Id);
        me.IsInvitationPending.ShouldBeFalse();
    }

    [Fact]
    public async Task InvitationLinkWorksOnlyOnce()
    {
        using var admin = await factory.CreateAdminClientAsync();
        using var client = factory.CreateClient();
        var email = AuthApi.NewEmail();
        (await admin.InviteUserAsync(email)).EnsureSuccessStatusCode();
        var token = factory.Emails.InvitationTokenFor(email);

        using var first = await client.AcceptInvitationAsync(email, token, AuthApi.ValidPassword);
        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var second = await client.AcceptInvitationAsync(email, token, "An0therPassword");
        second.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await second.ReadAsync<ProblemResponse>()).Code.ShouldBe("Users.InvitationInvalid");
    }

    [Fact]
    public async Task ResentInvitationCanBeAcceptedAndThenNoLongerResent()
    {
        using var admin = await factory.CreateAdminClientAsync();
        using var client = factory.CreateClient();
        var email = AuthApi.NewEmail();
        using var create = await admin.InviteUserAsync(email);
        create.EnsureSuccessStatusCode();
        var userId = (await create.ReadAsync<CreatedUserResponse>()).User.Id;

        using var resend = await admin.ResendInvitationAsync(userId);
        resend.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        factory.Emails.SentTo(email).Count.ShouldBe(2);

        using var accept = await client.AcceptInvitationAsync(email, factory.Emails.InvitationTokenFor(email), AuthApi.ValidPassword);
        accept.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var resendAfterAccepting = await admin.ResendInvitationAsync(userId);
        resendAfterAccepting.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await resendAfterAccepting.ReadAsync<ProblemResponse>()).Code.ShouldBe("Users.InvitationNotPending");
    }

    [Fact]
    public async Task AcceptingWithAWeakPasswordReturnsIdentityErrorCodes()
    {
        using var admin = await factory.CreateAdminClientAsync();
        using var client = factory.CreateClient();
        var email = AuthApi.NewEmail();
        (await admin.InviteUserAsync(email)).EnsureSuccessStatusCode();

        using var response = await client.AcceptInvitationAsync(email, factory.Emails.InvitationTokenFor(email), "short");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadAsync<ProblemResponse>()).Errors!.ShouldContainKey("PasswordTooShort");
    }

    [Fact]
    public async Task AcceptingWithAWrongTokenFails()
    {
        using var admin = await factory.CreateAdminClientAsync();
        using var client = factory.CreateClient();
        var email = AuthApi.NewEmail();
        (await admin.InviteUserAsync(email)).EnsureSuccessStatusCode();

        using var response = await client.AcceptInvitationAsync(email, "not-a-token", AuthApi.ValidPassword);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadAsync<ProblemResponse>()).Code.ShouldBe("Users.InvitationInvalid");
    }

    [Fact]
    public async Task CreatingAUserWithATakenEmailReturnsConflict()
    {
        var (userClient, email) = await factory.CreateUserClientAsync();
        userClient.Dispose();
        using var admin = await factory.CreateAdminClientAsync();

        using var response = await admin.InviteUserAsync(email);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadAsync<ProblemResponse>()).Code.ShouldBe("Users.EmailTaken");
    }

    [Fact]
    public async Task DeactivatedUserCannotLogInOrRefreshUntilReactivated()
    {
        using var admin = await factory.CreateAdminClientAsync();
        using var client = factory.CreateClient();
        var email = AuthApi.NewEmail();
        (await client.RegisterAsync(email)).EnsureSuccessStatusCode();
        var tokens = await client.LoginForTokensAsync(email);
        var userId = await admin.FindUserIdAsync(email);

        using var deactivate = await admin.DeactivateUserAsync(userId);
        deactivate.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var loginWhileInactive = await client.LoginAsync(email);
        loginWhileInactive.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await loginWhileInactive.ReadAsync<ProblemResponse>()).Detail.ShouldBe("LockedOut");

        using var refreshWhileInactive = await client.RefreshAsync(tokens.RefreshToken);
        refreshWhileInactive.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var activate = await admin.ActivateUserAsync(userId);
        activate.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var loginAgain = await client.LoginAsync(email);
        loginAgain.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeactivatingRejectsTheUsersCurrentAccessTokenAtOnce()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var (userClient, email) = await factory.CreateUserClientAsync();
        using var user = userClient;
        var userId = await admin.FindUserIdAsync(email);
        using var before = await user.GetAsync(new Uri("/api/users/me", UriKind.Relative), Ct);
        before.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await admin.DeactivateUserAsync(userId)).EnsureSuccessStatusCode();
        using var after = await user.GetAsync(new Uri("/api/users/me", UriKind.Relative), Ct);

        after.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RevokingAdminRejectsTheOldAdminTokenAtOnce()
    {
        using var admin = await factory.CreateAdminClientAsync();
        using var client = factory.CreateClient();
        var email = AuthApi.NewEmail();
        (await client.RegisterAsync(email)).EnsureSuccessStatusCode();
        var userId = await admin.FindUserIdAsync(email);
        (await admin.GrantAdminAsync(userId)).EnsureSuccessStatusCode();
        client.UseToken(await client.LoginForTokensAsync(email));

        (await admin.RevokeAdminAsync(userId)).EnsureSuccessStatusCode();
        using var adminEndpoint = await client.GetAsync(new Uri("/api/admin/users", UriKind.Relative), Ct);

        adminEndpoint.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PromotedUserGetsAdminRoleOnNextLogin()
    {
        using var admin = await factory.CreateAdminClientAsync();
        using var client = factory.CreateClient();
        var email = AuthApi.NewEmail();
        (await client.RegisterAsync(email)).EnsureSuccessStatusCode();
        var userId = await admin.FindUserIdAsync(email);

        using var promote = await admin.GrantAdminAsync(userId);
        promote.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        client.UseToken(await client.LoginForTokensAsync(email));
        using var adminEndpoint = await client.GetAsync(new Uri("/api/admin/users", UriKind.Relative), Ct);

        (await client.GetJsonAsync<UserResponse>("/api/users/me")).Roles.ShouldBe(["Admin"]);
        adminEndpoint.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("en", "You can't deactivate or reactivate your own account.")]
    [InlineData("es", "No puedes desactivar ni reactivar tu propia cuenta.")]
    public async Task AdminCannotDeactivateThemselves(string language, string expectedTitle)
    {
        using var admin = await factory.CreateAdminClientAsync();
        admin.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);
        var adminId = await admin.GetMyIdAsync();

        using var response = await admin.DeactivateUserAsync(adminId);
        var problem = await response.ReadAsync<ProblemResponse>();

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        problem.Code.ShouldBe("Users.CannotChangeOwnStatus");
        problem.Title.ShouldBe(expectedTitle);
    }

    [Fact]
    public async Task UnknownUserReturnsNotFound()
    {
        using var admin = await factory.CreateAdminClientAsync();

        using var response = await admin.DeactivateUserAsync(Guid.NewGuid());

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.ReadAsync<ProblemResponse>()).Code.ShouldBe("Users.NotFound");
    }
}
