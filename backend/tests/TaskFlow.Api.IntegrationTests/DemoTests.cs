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
using TaskFlow.Application.Users;
using TaskFlow.Infrastructure.Demo;
using TaskFlow.Infrastructure.Identity;
using TaskFlow.Domain.Notifications;
using TaskFlow.Domain.Tenancy;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.Infrastructure.Tenancy;

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
        await using var db = new AppDbContext(options, new TenantContext());
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
        status.TenantId.ShouldBeNull();
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
        status.TenantId.ShouldBe(TenantDefaults.DemoId);
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
            var stray = new ApplicationUser { UserName = strayEmail, Email = strayEmail };
            (await users.CreateAsync(stray, AuthApi.ValidPassword)).Succeeded.ShouldBeTrue();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TenantMemberships.Add(TenantMembership.Create(TenantDefaults.DemoId, stray.Id, TenantRole.Member, factory.Time.GetUtcNow()));
            await db.SaveChangesAsync(Ct);
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

        await host.Services.GetRequiredService<TenantScope>().RunAsync(TenantDefaults.DemoId, async (services, token) =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            (await db.Users.AnyAsync(user => user.Email == strayEmail, token)).ShouldBeFalse();
            (await db.Users.AnyAsync(user => user.Email == SqlServerApiFactory.AdminEmail, token)).ShouldBeTrue();
            (await db.Clients.AnyAsync(row => row.Name == "Leftover client", token)).ShouldBeFalse();
            (await db.Clients.CountAsync(token)).ShouldBe(4);
            (await db.Projects.CountAsync(token)).ShouldBe(4);
            (await db.Tasks.CountAsync(token)).ShouldBeGreaterThanOrEqualTo(25);
            (await db.Tasks.Select(task => task.Status).Distinct().CountAsync(token)).ShouldBe(4);
            (await db.TaskComments.AnyAsync(token)).ShouldBeTrue();
            (await db.TimeEntries.AnyAsync(token)).ShouldBeTrue();

            // The demo accounts are members of the demo tenant (the admin owns it) and are not super-admins.
            var members = await db.TenantMemberships.Where(m => m.TenantId == TenantDefaults.DemoId).ToListAsync(token);
            members.ShouldContain(m => m.Role == TenantRole.Owner);
            (await db.Users.Where(user => user.Email == AdminEmail || user.Email == MemberEmail)
                .SelectMany(user => db.UserRoles.Where(role => role.UserId == user.Id)).AnyAsync(token)).ShouldBeFalse();
        }, Ct);

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
    public async Task AResetNeverTouchesOtherTenantsOrTheirUsers()
    {
        using var host = DemoHost();
        using var client = host.CreateClient();
        await WaitForStartupResetAsync(host);

        // A production tenant that shares the database with the demo: a client, a project with a labelled task,
        // and a user who belongs to it and to the demo tenant.
        var acmeId = Guid.CreateVersion7();
        var acmeUserEmail = AuthApi.NewEmail();
        Guid acmeUserId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = acmeUserEmail, Email = acmeUserEmail };
            (await users.CreateAsync(user, AuthApi.ValidPassword)).Succeeded.ShouldBeTrue();
            acmeUserId = user.Id;
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Tenants.Add(Tenant.Create(acmeId, "Acme", "acme", isDemo: false, factory.Time.GetUtcNow()).Value);
            db.TenantMemberships.Add(TenantMembership.Create(acmeId, acmeUserId, TenantRole.Owner, factory.Time.GetUtcNow()));
            db.TenantMemberships.Add(TenantMembership.Create(TenantDefaults.DemoId, acmeUserId, TenantRole.Member, factory.Time.GetUtcNow()));
            db.NotificationPreferences.Add(NotificationPreferences.CreateDefault(acmeUserId));
            await db.SaveChangesAsync(Ct);
        }

        await host.Services.GetRequiredService<TenantScope>().RunAsync(acmeId, async (services, token) =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            db.Clients.Add(TaskFlow.Domain.Clients.Client.Create("Acme customer", null, null, factory.Time.GetUtcNow()));
            await db.SaveChangesAsync(token);
        }, Ct);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<DemoResetter>().ResetAsync(Ct)).ShouldBeTrue();
        }

        await host.Services.GetRequiredService<TenantScope>().RunAsync(acmeId, async (services, token) =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            (await db.Clients.CountAsync(row => row.Name == "Acme customer", token)).ShouldBe(1);
            (await db.Users.AnyAsync(user => user.Id == acmeUserId, token)).ShouldBeTrue();
            (await db.Tenants.AnyAsync(tenant => tenant.Id == acmeId, token)).ShouldBeTrue();
            (await db.TenantMemberships.AnyAsync(m => m.TenantId == acmeId && m.UserId == acmeUserId, token)).ShouldBeTrue();
            (await db.NotificationPreferences.AnyAsync(p => p.UserId == acmeUserId, token)).ShouldBeTrue();
        }, Ct);

        // The demo tenant itself was rebuilt, so the visitor-user who also belonged elsewhere is not a member of it any more.
        await host.Services.GetRequiredService<TenantScope>().RunAsync(TenantDefaults.DemoId, async (services, token) =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            (await db.Clients.AnyAsync(row => row.Name == "Acme customer", token)).ShouldBeFalse();
            (await db.Clients.CountAsync(token)).ShouldBe(4);
            (await db.TenantMemberships.AnyAsync(m => m.TenantId == TenantDefaults.DemoId && m.UserId == acmeUserId, token)).ShouldBeFalse();
        }, Ct);
    }

    [Fact]
    public async Task AResetAbortsAndChangesNothingWhenADemoAccountBelongsToAnotherTenant()
    {
        using var host = DemoHost();
        using var client = host.CreateClient();
        await WaitForStartupResetAsync(host);

        var otherTenantId = Guid.CreateVersion7();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var member = await db.Users.SingleAsync(user => user.Email == MemberEmail, Ct);
            db.Tenants.Add(Tenant.Create(otherTenantId, "Real agency", "real-agency", isDemo: false, factory.Time.GetUtcNow()).Value);
            db.TenantMemberships.Add(TenantMembership.Create(otherTenantId, member.Id, TenantRole.Owner, factory.Time.GetUtcNow()));
            await db.SaveChangesAsync(Ct);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            await Should.ThrowAsync<InvalidOperationException>(
                () => scope.ServiceProvider.GetRequiredService<DemoResetter>().ResetAsync(Ct));
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.TenantMemberships.CountAsync(m => m.TenantId == otherTenantId, Ct)).ShouldBe(1);
        }

        // The password was not overwritten with the published demo one.
        using var login = await client.LoginAsync(MemberEmail, MemberPassword);
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AResetAbortsWhenADemoAccountIsASuperAdmin()
    {
        using var host = DemoHost();
        using var client = host.CreateClient();
        await WaitForStartupResetAsync(host);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var member = await users.FindByEmailAsync(MemberEmail);
            (await users.AddToRoleAsync(member!, Roles.Admin)).Succeeded.ShouldBeTrue();
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            await Should.ThrowAsync<InvalidOperationException>(
                () => scope.ServiceProvider.GetRequiredService<DemoResetter>().ResetAsync(Ct));
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            (await users.IsInRoleAsync((await users.FindByEmailAsync(MemberEmail))!, Roles.Admin)).ShouldBeTrue();
        }
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

        // The user-management API is for the platform super-admin; the demo accounts do not have that role.
        using var admin = host.CreateClient();
        admin.UseToken(await admin.LoginForTokensAsync(SqlServerApiFactory.AdminEmail, SqlServerApiFactory.AdminPassword));
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
        admin.UseToken(await admin.LoginForTokensAsync(SqlServerApiFactory.AdminEmail, SqlServerApiFactory.AdminPassword));

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

    private sealed record DemoStatus(bool Enabled, DateTimeOffset? NextResetAt, IReadOnlyList<DemoAccount> Accounts, Guid? TenantId);

    private sealed record DemoAccount(string Email, string Password, string Role);

    private sealed record ClientRow(Guid Id, string Name);
}
