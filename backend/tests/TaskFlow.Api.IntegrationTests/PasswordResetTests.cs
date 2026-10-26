using System.Net;
using System.Net.Http.Headers;
using TaskFlow.Api.IntegrationTests.Infrastructure;

namespace TaskFlow.Api.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class PasswordResetTests(SqlServerApiFactory factory)
{
    private const string NewPassword = "N3wPassword";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ForgotPasswordEmailsALinkThatSetsANewPassword()
    {
        using var client = factory.CreateClient();
        var email = await CreateUserWithPasswordAsync();

        using var forgot = await client.ForgotPasswordAsync(email);
        forgot.StatusCode.ShouldBe(HttpStatusCode.OK);
        factory.Emails.SentTo(email)[^1].TextBody.ShouldContain("http://localhost:4200/reset-password#email=");

        using var reset = await client.ResetPasswordAsync(email, factory.Emails.ResetCodeFor(email), NewPassword);
        reset.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var oldPassword = await client.LoginAsync(email);
        oldPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var newPassword = await client.LoginAsync(email, NewPassword);
        newPassword.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ResetLinkWorksOnlyOnce()
    {
        using var client = factory.CreateClient();
        var email = await CreateUserWithPasswordAsync();
        (await client.ForgotPasswordAsync(email)).EnsureSuccessStatusCode();
        var code = factory.Emails.ResetCodeFor(email);
        (await client.ResetPasswordAsync(email, code, NewPassword)).EnsureSuccessStatusCode();

        using var again = await client.ResetPasswordAsync(email, code, "An0therPassword");

        again.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await again.ReadAsync<ProblemResponse>()).Errors!.Keys.ShouldContain("InvalidToken");
    }

    [Fact]
    public async Task ResettingRejectsTheUsersCurrentTokensAtOnce()
    {
        using var client = factory.CreateClient();
        var email = await CreateUserWithPasswordAsync();
        using var user = factory.CreateClient();
        var tokens = await user.LoginForTokensAsync(email);
        user.UseToken(tokens);
        using var before = await user.GetAsync(new Uri("/api/users/me", UriKind.Relative), Ct);
        before.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await client.ForgotPasswordAsync(email)).EnsureSuccessStatusCode();
        (await client.ResetPasswordAsync(email, factory.Emails.ResetCodeFor(email), NewPassword)).EnsureSuccessStatusCode();

        using var after = await user.GetAsync(new Uri("/api/users/me", UriKind.Relative), Ct);
        after.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var refresh = await client.RefreshAsync(tokens.RefreshToken);
        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UnknownEmailGetsTheSameAnswerAndNoEmail()
    {
        using var client = factory.CreateClient();
        var email = AuthApi.NewEmail();

        using var forgot = await client.ForgotPasswordAsync(email);

        forgot.StatusCode.ShouldBe(HttpStatusCode.OK);
        factory.Emails.SentTo(email).ShouldBeEmpty();
    }

    [Fact]
    public async Task DeactivatedUserGetsNoEmail()
    {
        using var admin = await factory.CreateAdminClientAsync();
        using var client = factory.CreateClient();
        var email = await CreateUserWithPasswordAsync();
        (await admin.DeactivateUserAsync(await admin.FindUserIdAsync(email))).EnsureSuccessStatusCode();
        var sentBefore = factory.Emails.SentTo(email).Count;

        using var forgot = await client.ForgotPasswordAsync(email);

        forgot.StatusCode.ShouldBe(HttpStatusCode.OK);
        factory.Emails.SentTo(email).Count.ShouldBe(sentBefore);
    }

    [Fact]
    public async Task EmailIsWrittenInTheLanguageOfTheRequest()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("es"));
        var email = await CreateUserWithPasswordAsync();

        (await client.ForgotPasswordAsync(email)).EnsureSuccessStatusCode();

        factory.Emails.SentTo(email)[^1].Subject.ShouldBe("Restablece tu contraseña de TaskFlow");
    }

    /// <summary>An invited user who accepted: their email is confirmed, which Identity requires to send a reset link.</summary>
    private async Task<string> CreateUserWithPasswordAsync()
    {
        using var admin = await factory.CreateAdminClientAsync();
        using var client = factory.CreateClient();
        var email = AuthApi.NewEmail();
        (await admin.InviteUserAsync(email)).EnsureSuccessStatusCode();
        (await client.AcceptInvitationAsync(email, factory.Emails.InvitationTokenFor(email), AuthApi.ValidPassword)).EnsureSuccessStatusCode();
        return email;
    }
}
