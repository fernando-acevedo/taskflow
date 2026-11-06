using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using TaskFlow.Api.IntegrationTests.Infrastructure;
using TaskFlow.Application.Demo;
using TaskFlow.Infrastructure.Demo;
using TaskFlow.Infrastructure.Identity;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Api.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class DemoTests(SqlServerApiFactory factory) : IAsyncLifetime
{
    private const string AdminEmail = "demo.admin@taskflow.demo";
    private const string AdminPassword = "DemoAdmin12345";
    private const string MemberEmail = "demo.member@taskflow.demo";
    private const string MemberPassword = "DemoMember12345";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // The reset deletes everything, so demo tests never touch the database the other test classes share: each
    // test gets its own database on the same SQL Server instance and drops it afterwards.
    private string? _connectionString;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_connectionString is null)
        {
            return;
        }

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(_connectionString).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureDeletedAsync();
    }

    /// <summary>A demo host; with <paramref name="clock"/> it runs on its own clock instead of the shared one, which can't go back.</summary>
    private WebApplicationFactory<Program> DemoHost(FakeTimeProvider? clock = null) => factory.WithWebHostBuilder(builder =>
    {
        if (clock is not null)
        {
            builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(clock));
        }

        _connectionString ??= factory.NewDatabaseConnectionString("TaskFlowDemoTests");
        builder.UseSetting("ConnectionStrings:TaskFlow", _connectionString);
        builder.UseSetting("Demo:Enabled", "true");
        builder.UseSetting("Demo:AllowDataWipe", "true");
        builder.UseSetting("Demo:Accounts:0:Password", AdminPassword);
        builder.UseSetting("Demo:Accounts:1:Password", MemberPassword);
        builder.UseSetting("Auth:AllowSelfRegistration", "false");
    });

    [Fact]
    public async Task StatusIsEmptyWhenTheDemoIsDisabled()
    {
        using var client = factory.CreateClient();

        var status = await client.GetJsonAsync<DemoStatus>("/api/demo/status");

        status.Enabled.ShouldBeFalse();
        status.NextResetAt.ShouldBeNull();
        status.Accounts.ShouldBeEmpty();
    }

    [Fact]
    public async Task StatusListsTheAccountsAndTheNextResetWhenEnabled()
    {
        using var host = DemoHost();
        using var client = host.CreateClient();

        var status = await client.GetJsonAsync<DemoStatus>("/api/demo/status");

        status.Enabled.ShouldBeTrue();
        // The shared clock drifts with other tests that advance it, so derive the expectation from it.
        status.NextResetAt.ShouldBe(DemoSchedule.NextOccurrence("0 3 * * *", "UTC", factory.Time.GetUtcNow()));
        status.Accounts.ShouldContain(account => account.Email == AdminEmail && account.Password == AdminPassword && account.Role == "Admin");
        status.Accounts.ShouldContain(account => account.Email == MemberEmail && account.Password == MemberPassword && account.Role == "Member");
    }

    [Fact]
    public async Task AResetReplacesTheDataRemovesOtherUsersAndLetsTheDemoAccountsLogIn()
    {
        using var host = DemoHost();
        using var client = host.CreateClient();
        await WaitForStartupResetAsync(host);

        // Whatever a visitor or an earlier test left behind: a stray user and an old client.
        var strayEmail = AuthApi.NewEmail();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            (await users.CreateAsync(new ApplicationUser { UserName = strayEmail, Email = strayEmail }, AuthApi.ValidPassword)).Succeeded.ShouldBeTrue();
        }

        using var admin = host.CreateClient();
        admin.UseToken(await admin.LoginForTokensAsync(AdminEmail, AdminPassword));
        using (var response = await admin.PostAsJsonAsync("/api/clients", new { name = "Leftover client" }, Ct))
        {
            response.EnsureSuccessStatusCode();
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reset = await scope.ServiceProvider.GetRequiredService<DemoResetter>().ResetAsync(Ct);
            reset.ShouldBeTrue();
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Users.AnyAsync(user => user.Email == strayEmail, Ct)).ShouldBeFalse();
            (await db.Users.AnyAsync(user => user.Email == SqlServerApiFactory.AdminEmail, Ct)).ShouldBeTrue();
            (await db.Clients.AnyAsync(row => row.Name == "Leftover client", Ct)).ShouldBeFalse();
            (await db.Clients.CountAsync(Ct)).ShouldBe(4);
            (await db.Projects.CountAsync(Ct)).ShouldBe(4);
            (await db.Tasks.CountAsync(Ct)).ShouldBeGreaterThanOrEqualTo(25);
            (await db.Tasks.Select(task => task.Status).Distinct().CountAsync(Ct)).ShouldBe(4);
            (await db.TaskComments.AnyAsync(Ct)).ShouldBeTrue();
            (await db.TimeEntries.AnyAsync(Ct)).ShouldBeTrue();
        }

        // The old session ended (the stamp rotated) and both demo accounts log in with their configured passwords.
        using (var stale = await admin.GetAsync("/api/clients", Ct))
        {
            stale.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        foreach (var (email, password) in new[] { (AdminEmail, AdminPassword), (MemberEmail, MemberPassword) })
        {
            using var login = await client.LoginAsync(email, password);
            login.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var fresh = host.CreateClient();
        fresh.UseToken(await fresh.LoginForTokensAsync(AdminEmail, AdminPassword));
        var clients = await fresh.GetJsonAsync<PagedResponse<ClientRow>>("/api/clients");
        clients.TotalCount.ShouldBe(4);
    }

    [Fact]
    public async Task TheBackgroundServiceResetsOncePerScheduledOccurrence()
    {
        var clock = new FakeTimeProvider(factory.Time.GetUtcNow());
        using var host = DemoHost(clock);
        using var client = host.CreateClient();
        await WaitForStartupResetAsync(host);
        var before = await CountResetsAsync(host);

        // A day later the service's next check finds a scheduled reset it has not done yet, and does it once.
        clock.Advance(TimeSpan.FromDays(1));
        var timer = Stopwatch.StartNew();
        while (await CountResetsAsync(host) == before && timer.Elapsed < TimeSpan.FromSeconds(90))
        {
            await Task.Delay(250, Ct);
        }

        (await CountResetsAsync(host)).ShouldBe(before + 1);
        await using var scope = host.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<DemoResetter>().ResetIfDueAsync(Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task SharedAccountsCannotBeDeactivatedAndIdentityLockoutRoutesAreClosed()
    {
        using var host = DemoHost();
        using var client = host.CreateClient();
        await WaitForStartupResetAsync(host);

        using var admin = host.CreateClient();
        admin.UseToken(await admin.LoginForTokensAsync(AdminEmail, AdminPassword));
        var memberId = await admin.FindUserIdAsync(MemberEmail);

        using (var deactivate = await admin.DeactivateUserAsync(memberId))
        {
            deactivate.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await deactivate.ReadAsync<ProblemResponse>()).Code.ShouldBe("Demo.AccountProtected");
        }

        using (var changePassword = await admin.PostAsJsonAsync("/api/auth/manage/info", new { newPassword = "Hacked12345" }, Ct))
        {
            changePassword.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await changePassword.ReadAsync<ProblemResponse>()).Code.ShouldBe("Demo.ActionDisabled");
        }

        using var forgot = await client.ForgotPasswordAsync(MemberEmail);
        forgot.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var info = await admin.GetAsync("/api/auth/manage/info", Ct);
        info.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task VisitorsCannotLockTheDemoAccountsOutByFailingLogins()
    {
        using var host = DemoHost();
        using var client = host.CreateClient();
        await WaitForStartupResetAsync(host);

        foreach (var email in new[] { AdminEmail, MemberEmail })
        {
            for (var attempt = 0; attempt < 6; attempt++)
            {
                using var wrong = await client.LoginAsync(email, "Wrong12345");
                wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            }
        }

        using var admin = await client.LoginAsync(AdminEmail, AdminPassword);
        admin.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var member = await client.LoginAsync(MemberEmail, MemberPassword);
        member.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AdminsCannotCreateOrInviteUsersInTheDemo()
    {
        using var host = DemoHost();
        using var client = host.CreateClient();
        await WaitForStartupResetAsync(host);
        using var admin = host.CreateClient();
        admin.UseToken(await admin.LoginForTokensAsync(AdminEmail, AdminPassword));

        var invitee = AuthApi.NewEmail();
        using (var invite = await admin.InviteUserAsync(invitee, isAdmin: true))
        {
            invite.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await invite.ReadAsync<ProblemResponse>()).Code.ShouldBe("Demo.ActionDisabled");
        }

        factory.Emails.SentTo(invitee).ShouldBeEmpty();

        var memberId = await admin.FindUserIdAsync(MemberEmail);
        using (var resend = await admin.ResendInvitationAsync(memberId))
        {
            resend.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await resend.ReadAsync<ProblemResponse>()).Code.ShouldBe("Demo.ActionDisabled");
        }
    }

    [Theory]
    [InlineData("/api/auth/manage/info", """{"newEmail":"someone@example.com"}""")]
    [InlineData("/api/auth/manage/info", """{"newPassword":"Hacked12345","oldPassword":"x"}""")]
    [InlineData("/api/auth/manage/2fa", """{"enable":true}""")]
    [InlineData("/api/auth/resendConfirmationEmail", """{"email":"demo.member@taskflow.demo"}""")]
    [InlineData("/api/auth/forgotPassword", """{"email":"demo.member@taskflow.demo"}""")]
    [InlineData("/api/auth/resetPassword", """{"email":"demo.member@taskflow.demo","resetCode":"x","newPassword":"Hacked12345"}""")]
    public async Task IdentityRoutesThatCouldLockOthersOutAreClosed(string path, string json)
    {
        using var host = DemoHost();
        using var admin = host.CreateClient();
        await WaitForStartupResetAsync(host);
        admin.UseToken(await admin.LoginForTokensAsync(AdminEmail, AdminPassword));

        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using var response = await admin.PostAsync(new Uri(path, UriKind.Relative), content, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.ReadAsync<ProblemResponse>()).Code.ShouldBe("Demo.ActionDisabled");
    }

    /// <summary>The hosted service resets at startup (nothing was ever reset), in the background.</summary>
    private static async Task WaitForStartupResetAsync(WebApplicationFactory<Program> host)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(90))
        {
            await using var scope = host.Services.CreateAsyncScope();
            if (await scope.ServiceProvider.GetRequiredService<AppDbContext>().DemoResetLogs.AnyAsync(Ct))
            {
                return;
            }

            await Task.Delay(250, Ct);
        }

        throw new TimeoutException("The demo did not reset at startup.");
    }

    private static async Task<int> CountResetsAsync(WebApplicationFactory<Program> host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().DemoResetLogs.CountAsync(Ct);
    }

    private sealed record DemoStatus(bool Enabled, DateTimeOffset? NextResetAt, IReadOnlyList<DemoAccount> Accounts);

    private sealed record DemoAccount(string Email, string Password, string Role);

    private sealed record ClientRow(Guid Id, string Name);
}
