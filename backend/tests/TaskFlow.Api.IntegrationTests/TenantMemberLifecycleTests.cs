using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Api.IntegrationTests.Infrastructure;
using TaskFlow.Application.Notifications;
using TaskFlow.Domain.Notifications;
using TaskFlow.Domain.Tenancy;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.Infrastructure.Tenancy;

namespace TaskFlow.Api.IntegrationTests;

/// <summary>Accounts are global, memberships are not: what leaving, inviting and adding people must not leak or leave behind (ADR 0013).</summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class TenantMemberLifecycleTests(SqlServerApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AProjectOwnerCannotAddAnAccountThatOnlyBelongsToAnotherTenant()
    {
        using var a = await NewTenantAsync();
        using var b = await NewTenantAsync();
        var client = await a.Owner.CreateClientAsync();
        var project = await a.Owner.CreateProjectAsync(client.Id);

        using var foreign = await a.Owner.AddMemberAsync(project.Id, b.MemberEmail);
        using var unknown = await a.Owner.AddMemberAsync(project.Id, AuthApi.NewEmail());
        using var own = await a.Owner.AddMemberAsync(project.Id, a.MemberEmail);

        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await foreign.ReadAsync<ProblemResponse>()).Code.ShouldBe((await unknown.ReadAsync<ProblemResponse>()).Code);
        own.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task RemovingAMemberTakesThemOutOfProjectsUnassignsTheirTasksAndStopsDueSoon()
    {
        using var a = await NewTenantAsync();
        var client = await a.Owner.CreateClientAsync();
        var project = await a.Owner.CreateProjectAsync(client.Id);
        (await a.Owner.AddMemberAsync(project.Id, a.MemberEmail)).EnsureSuccessStatusCode();
        var memberId = await a.Member.GetMyIdAsync();
        var ownerId = await a.Owner.GetMyIdAsync();
        var task = await a.Owner.CreateTaskAsync(project.Id, new { title = "Due", assigneeId = memberId, dueDate = factory.Today });

        // The member is also the only Owner of a second project: the Owner who removes them takes it over.
        var soloProject = await a.Member.CreateProjectAsync(client.Id, "Solo");

        using var removed = await a.Owner.DeleteAsync(new Uri($"/api/tenant/members/{memberId}", UriKind.Relative), Ct);
        removed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var members = await a.Owner.GetJsonAsync<List<MemberResponse>>($"/api/projects/{project.Id}/members");
        members.ShouldNotContain(m => m.UserId == memberId);
        var soloMembers = await a.Owner.GetJsonAsync<List<MemberResponse>>($"/api/projects/{soloProject.Id}/members");
        var solo = soloMembers.ShouldHaveSingleItem();
        solo.UserId.ShouldBe(ownerId);
        solo.Role.ShouldBe("Owner");
        var tasks = await a.Owner.GetJsonAsync<List<TaskResponse>>($"/api/projects/{project.Id}/tasks");
        tasks.Single(t => t.Id == task.Id).AssigneeId.ShouldBeNull();

        // Even if a stale assignment were left behind, no due-soon notice goes to someone outside the tenant.
        await ScopedAsync(a.TenantId, async db =>
        {
            await db.Tasks.Where(t => t.Id == task.Id).ExecuteUpdateAsync(set => set.SetProperty(t => t.AssigneeId, memberId), Ct);
            return 0;
        });
        (await RunDueSoonAsync(a.TenantId)).ShouldBe(0);
        (await ScopedAsync(a.TenantId, db => db.Notifications.CountAsync(n => n.UserId == memberId && n.Type == NotificationType.TaskDueSoon, Ct))).ShouldBe(0);
    }

    [Fact]
    public async Task InvitingAnExistingAccountAnswersLikeInvitingANewOne()
    {
        using var a = await NewTenantAsync();
        using var b = await NewTenantAsync();
        var freshEmail = AuthApi.NewEmail();

        using var existing = await a.Owner.PostAsJsonAsync("/api/tenant/members", new { email = b.MemberEmail, role = "Member" }, Ct);
        using var fresh = await a.Owner.PostAsJsonAsync("/api/tenant/members", new { email = freshEmail, role = "Member" }, Ct);

        existing.StatusCode.ShouldBe(HttpStatusCode.Created);
        var known = await existing.ReadAsync<InvitedResponse>();
        var created = await fresh.ReadAsync<InvitedResponse>();
        known.AccountCreated.ShouldBe(created.AccountCreated);
        known.Member.IsInvitationPending.ShouldBe(created.Member.IsInvitationPending);
        known.Member.IsActive.ShouldBe(created.Member.IsActive);
        known.Member.DisplayName.ShouldBe(b.MemberEmail[..b.MemberEmail.IndexOf('@', StringComparison.Ordinal)]);
    }

    [Fact]
    public async Task TwoOwnersRemovingEachOtherNeverLeaveTheTenantWithoutAnOwner()
    {
        using var a = await NewTenantAsync();
        var (secondClient, secondEmail) = await factory.CreateUserClientAsync(inDefaultTenant: false);
        using var second = secondClient;
        await factory.AddToTenantAsync(secondEmail, a.TenantId, TenantRole.Owner);
        second.DefaultRequestHeaders.Add("X-Tenant", a.TenantId.ToString());
        var ownerId = await a.Owner.GetMyIdAsync();
        var secondId = await second.GetMyIdAsync();

        var calls = await Task.WhenAll(
            a.Owner.DeleteAsync(new Uri($"/api/tenant/members/{secondId}", UriKind.Relative), Ct),
            second.DeleteAsync(new Uri($"/api/tenant/members/{ownerId}", UriKind.Relative), Ct));

        calls.Count(call => call.StatusCode == HttpStatusCode.NoContent).ShouldBeLessThanOrEqualTo(1);
        (await ScopedAsync(a.TenantId, db => db.TenantMemberships.CountAsync(m => m.TenantId == a.TenantId && m.Role == TenantRole.Owner, Ct)))
            .ShouldBeGreaterThanOrEqualTo(1);
        foreach (var call in calls)
        {
            call.Dispose();
        }
    }

    [Fact]
    public async Task ASuperAdminWhoIsAMemberUsesTheirRealRoleAndLeavesNoSupportEntry()
    {
        using var a = await NewTenantAsync();
        await factory.AddToTenantAsync(SqlServerApiFactory.AdminEmail, a.TenantId);
        try
        {
            using var admin = await factory.CreateAdminClientAsync();
            admin.DefaultRequestHeaders.Add("X-Tenant", a.TenantId.ToString());

            using var members = await admin.GetAsync(new Uri("/api/tenant/members", UriKind.Relative), Ct);

            members.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
        finally
        {
            // The shared super-admin must not keep a second membership: it would end its default tenant for other tests.
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var adminId = await db.Users.Where(user => user.Email == SqlServerApiFactory.AdminEmail).Select(user => user.Id).SingleAsync(Ct);
            await db.TenantMemberships.Where(m => m.TenantId == a.TenantId && m.UserId == adminId).ExecuteDeleteAsync(Ct);
            scope.ServiceProvider.GetRequiredService<TenantAccess>().Forget(adminId, a.TenantId);
        }

        var entries = await a.Owner.GetJsonAsync<List<SupportEntry>>("/api/tenant/support-access");
        entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task ALabelOfAnotherTenantCannotBeAttachedToATask()
    {
        using var a = await NewTenantAsync();
        using var b = await NewTenantAsync();
        var projectA = await a.Owner.CreateProjectAsync((await a.Owner.CreateClientAsync()).Id);
        var projectB = await b.Owner.CreateProjectAsync((await b.Owner.CreateClientAsync()).Id);
        using var label = await a.Owner.PostAsJsonAsync($"/api/projects/{projectA.Id}/labels", new { name = "Bug", color = "#FF0000" }, Ct);
        var labelId = (await label.ReadAsync<IdResponse>()).Id;

        using var create = await b.Owner.PostAsJsonAsync(
            $"/api/projects/{projectB.Id}/tasks", new { title = "Task", labelIds = new[] { labelId } }, Ct);

        create.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await create.ReadAsync<ProblemResponse>()).Code.ShouldBe("Labels.NotInProject");
    }

    private async Task<int> RunDueSoonAsync(Guid tenantId) =>
        await factory.Services.GetRequiredService<TenantScope>().RunAsync(
            tenantId,
            async (services, token) =>
            {
                var result = await services.GetRequiredService<NotifyDueSoonHandler>().HandleAsync(token);
                result.IsSuccess.ShouldBeTrue();
                return result.Value;
            },
            Ct);

    private async Task<T> ScopedAsync<T>(Guid tenantId, Func<AppDbContext, Task<T>> work) =>
        await factory.Services.GetRequiredService<TenantScope>().RunAsync(
            tenantId, (services, _) => work(services.GetRequiredService<AppDbContext>()), Ct);

    private static string NewSlug() => $"t-{Guid.NewGuid():N}"[..20];

    /// <summary>A tenant with an Owner and a Member, both sending <c>X-Tenant</c>.</summary>
    private async Task<TenantSetup> NewTenantAsync()
    {
        using var admin = await factory.CreateAdminClientAsync();
        using var created = await admin.PostAsJsonAsync("/api/tenants", new { name = "Agency", slug = NewSlug() }, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var tenantId = (await created.ReadAsync<CreatedTenant>()).Tenant.Id;

        var (owner, ownerEmail) = await factory.CreateUserClientAsync(inDefaultTenant: false);
        var (member, memberEmail) = await factory.CreateUserClientAsync(inDefaultTenant: false);
        await factory.AddToTenantAsync(ownerEmail, tenantId, TenantRole.Owner);
        await factory.AddToTenantAsync(memberEmail, tenantId);
        owner.DefaultRequestHeaders.Add("X-Tenant", tenantId.ToString());
        member.DefaultRequestHeaders.Add("X-Tenant", tenantId.ToString());
        return new TenantSetup(tenantId, owner, member, memberEmail);
    }

    private sealed record TenantSetup(Guid TenantId, HttpClient Owner, HttpClient Member, string MemberEmail) : IDisposable
    {
        public void Dispose()
        {
            Owner.Dispose();
            Member.Dispose();
        }
    }

    private sealed record IdResponse(Guid Id);

    private sealed record CreatedTenant(IdResponse Tenant);

    private sealed record InvitedMember(Guid UserId, string Email, string DisplayName, string Role, bool IsActive, bool IsInvitationPending);

    private sealed record InvitedResponse(InvitedMember Member, bool AccountCreated, bool InvitationSent);

    private sealed record SupportEntry(Guid Id, Guid UserId, string Email);
}
