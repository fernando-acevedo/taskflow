using System.Net;
using System.Net.Http.Json;
using TaskFlow.Api.IntegrationTests.Infrastructure;

namespace TaskFlow.Api.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class EmailConfirmationTests(SqlServerApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RegisteringEmailsALinkThatConfirmsTheEmailSoPasswordResetsAreSent()
    {
        using var client = factory.CreateClient();
        var email = AuthApi.NewEmail();
        (await client.RegisterAsync(email)).EnsureSuccessStatusCode();
        factory.Emails.SentTo(email).ShouldHaveSingleItem().TextBody.ShouldContain("http://localhost:4200/confirm-email#userId=");

        (await client.ForgotPasswordAsync(email)).EnsureSuccessStatusCode();
        factory.Emails.SentTo(email).Count.ShouldBe(1);

        var (userId, code) = factory.Emails.ConfirmationFor(email);
        using var confirm = await client.ConfirmEmailAsync(userId, code);
        confirm.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await client.ForgotPasswordAsync(email)).EnsureSuccessStatusCode();
        factory.Emails.ResetCodeFor(email).ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task ProfileShowsWhetherTheEmailIsConfirmedAndAResentLinkConfirmsIt()
    {
        var (userClient, email) = await factory.CreateUserClientAsync();
        using var user = userClient;
        (await user.GetJsonAsync<UserResponse>("/api/users/me")).IsEmailConfirmed.ShouldBeFalse();

        using var resend = await user.ResendConfirmationEmailAsync(email);
        resend.StatusCode.ShouldBe(HttpStatusCode.OK);
        factory.Emails.SentTo(email).Count.ShouldBe(2);

        var (userId, code) = factory.Emails.ConfirmationFor(email);
        (await user.ConfirmEmailAsync(userId, code)).EnsureSuccessStatusCode();
        (await user.GetJsonAsync<UserResponse>("/api/users/me")).IsEmailConfirmed.ShouldBeTrue();
    }

    [Fact]
    public async Task ConfirmingWithAWrongCodeFails()
    {
        using var client = factory.CreateClient();
        var email = AuthApi.NewEmail();
        (await client.RegisterAsync(email)).EnsureSuccessStatusCode();
        var (userId, _) = factory.Emails.ConfirmationFor(email);

        using var confirm = await client.ConfirmEmailAsync(userId, "d3JvbmctY29kZQ");

        confirm.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EmailChangesAreNotSent()
    {
        var (userClient, _) = await factory.CreateUserClientAsync();
        using var user = userClient;
        var newEmail = AuthApi.NewEmail();

        using var change = await user.PostAsJsonAsync("/api/auth/manage/info", new { newEmail }, Ct);

        change.StatusCode.ShouldBe(HttpStatusCode.OK);
        factory.Emails.SentTo(newEmail).ShouldBeEmpty();
    }
}
