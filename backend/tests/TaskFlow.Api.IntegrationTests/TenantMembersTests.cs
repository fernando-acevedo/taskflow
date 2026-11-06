using System.Net;
using System.Net.Http.Json;
using TaskFlow.Api.IntegrationTests.Infrastructure;

namespace TaskFlow.Api.IntegrationTests;

// Written for T-62; they run once the tenancy migration (T-61) exists.
[Collection(SqlServerTestGroup.Name)]
public sealed class TenantMembersTests(SqlServerApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SuperAdminCreatesATenantWithAnInvitedOwner()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var ownerEmail = AuthApi.NewEmail();

        using var response = await admin.PostAsJsonAsync(
            "/api/tenants", new { name = "Acme", slug = NewSlug(), ownerEmail, ownerDisplayName = "Olivia Owner" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await response.ReadAsync<CreatedTenantResponse>();
        created.Owner.ShouldNotBeNull();
        created.Owner.AccountCreated.ShouldBeTrue();
        created.Owner.InvitationSent.ShouldBeTrue();
        created.Owner.Member.Role.ShouldBe("Owner");
        created.Owner.Member.IsInvitationPending.ShouldBeTrue();

        using var owner = await AcceptAsync(ownerEmail, created.Tenant.Id);
        var mine = await owner.GetJsonAsync<List<MyTenantResponse>>("/api/tenants/mine");
        mine.ShouldHaveSingleItem().Role.ShouldBe("Owner");
    }

    [Fact]
    public async Task OnlySuperAdminCreatesOrListsAllTenants()
    {
        var (userClient, _) = await factory.CreateUserClientAsync();
        using var user = userClient;

        using var create = await user.PostAsJsonAsync("/api/tenants", new { name = "Nope", slug = NewSlug() }, Ct);
        using var list = await user.GetAsync(new Uri("/api/tenants", UriKind.Relative), Ct);
        using var mine = await user.GetAsync(new Uri("/api/tenants/mine", UriKind.Relative), Ct);

        create.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        list.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        mine.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SlugMustBeValidAndUnique()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var slug = NewSlug();
        (await admin.PostAsJsonAsync("/api/tenants", new { name = "First", slug }, Ct)).EnsureSuccessStatusCode();

        using var taken = await admin.PostAsJsonAsync("/api/tenants", new { name = "Second", slug }, Ct);
        using var invalid = await admin.PostAsJsonAsync("/api/tenants", new { name = "Third", slug = "Not A Slug" }, Ct);

        taken.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await taken.ReadAsync<ProblemResponse>()).Code.ShouldBe("Tenants.SlugTaken");
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await invalid.ReadAsync<ProblemResponse>()).Code.ShouldBe("Tenants.InvalidSlug");
    }

    [Fact]
    public async Task InvitingAnExistingAccountOnlyAddsTheMembership()
    {
        var (tenantId, owner, _) = await CreateTenantWithOwnerAsync();
        using var ownerClient = owner;
        var (existingClient, existingEmail) = await factory.CreateUserClientAsync();
        using var existing = existingClient;

        using var response = await ownerClient.PostAsJsonAsync("/api/tenant/members", new { email = existingEmail, role = "Member" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var invited = await response.ReadAsync<InvitedMemberResponse>();
        invited.AccountCreated.ShouldBeTrue();
        invited.Member.Role.ShouldBe("Member");
        factory.Emails.SentTo(existingEmail).ShouldNotContain(message => message.TextBody.Contains("accept-invitation", StringComparison.Ordinal));

        var mine = await existing.GetJsonAsync<List<MyTenantResponse>>("/api/tenants/mine");
        mine.ShouldContain(tenant => tenant.Id == tenantId && tenant.Role == "Member");

        using var again = await ownerClient.PostAsJsonAsync("/api/tenant/members", new { email = existingEmail, role = "Member" }, Ct);
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await again.ReadAsync<ProblemResponse>()).Code.ShouldBe("Tenants.AlreadyMember");
    }

    [Fact]
    public async Task InvitingANewEmailCreatesTheAccountAndSendsTheInvitation()
    {
        var (_, owner, _) = await CreateTenantWithOwnerAsync();
        using var ownerClient = owner;
        var email = AuthApi.NewEmail();

        using var response = await ownerClient.PostAsJsonAsync("/api/tenant/members", new { email, role = "Admin", displayName = "Ana Admin" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var invited = await response.ReadAsync<InvitedMemberResponse>();
        invited.AccountCreated.ShouldBeTrue();
        invited.Member.IsInvitationPending.ShouldBeTrue();
        invited.Member.Role.ShouldBe("Admin");
        factory.Emails.InvitationTokenFor(email).ShouldNotBeEmpty();

        using var resend = await ownerClient.PostAsync(new Uri($"/api/tenant/members/{invited.Member.UserId}/invitation", UriKind.Relative), null, Ct);
        resend.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task TheLastOwnerCannotBeRemovedOrDemoted()
    {
        var (tenantId, owner, ownerId) = await CreateTenantWithOwnerAsync();
        owner.Dispose();
        using var admin = await factory.CreateAdminClientAsync();
        admin.DefaultRequestHeaders.Add("X-Tenant", tenantId.ToString());

        using var demote = await admin.PutAsJsonAsync($"/api/tenant/members/{ownerId}", new { role = "Member" }, Ct);
        using var remove = await admin.DeleteAsync(new Uri($"/api/tenant/members/{ownerId}", UriKind.Relative), Ct);

        demote.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await demote.ReadAsync<ProblemResponse>()).Code.ShouldBe("Tenants.LastOwner");
        remove.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await remove.ReadAsync<ProblemResponse>()).Code.ShouldBe("Tenants.LastOwner");
    }

    [Fact]
    public async Task AnOwnerCanChangeRolesAndRemoveMembersAndItTakesEffectAtOnce()
    {
        var (tenantId, owner, _) = await CreateTenantWithOwnerAsync();
        using var ownerClient = owner;
        var (memberClient, email) = await factory.CreateUserClientAsync();
        using var member = memberClient;
        var memberId = await member.GetMyIdAsync();
        (await ownerClient.PostAsJsonAsync("/api/tenant/members", new { email, role = "Member" }, Ct)).EnsureSuccessStatusCode();
        member.DefaultRequestHeaders.Add("X-Tenant", tenantId.ToString());

        using var asMember = await member.GetAsync(new Uri("/api/tenant/members", UriKind.Relative), Ct);
        asMember.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var promote = await ownerClient.PutAsJsonAsync($"/api/tenant/members/{memberId}", new { role = "Admin" }, Ct);
        promote.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var asAdmin = await member.GetAsync(new Uri("/api/tenant/members", UriKind.Relative), Ct);
        asAdmin.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var remove = await ownerClient.DeleteAsync(new Uri($"/api/tenant/members/{memberId}", UriKind.Relative), Ct);
        remove.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var afterRemoval = await member.GetAsync(new Uri("/api/tenant/members", UriKind.Relative), Ct);
        afterRemoval.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var removeAgain = await ownerClient.DeleteAsync(new Uri($"/api/tenant/members/{memberId}", UriKind.Relative), Ct);
        removeAgain.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AnAdminCannotCreateOrModifyOwners()
    {
        var (tenantId, owner, ownerId) = await CreateTenantWithOwnerAsync();
        using var ownerClient = owner;
        var adminEmail = AuthApi.NewEmail();
        (await ownerClient.PostAsJsonAsync("/api/tenant/members", new { email = adminEmail, role = "Admin" }, Ct)).EnsureSuccessStatusCode();
        using var tenantAdmin = await AcceptAsync(adminEmail, tenantId);

        using var inviteOwner = await tenantAdmin.PostAsJsonAsync("/api/tenant/members", new { email = AuthApi.NewEmail(), role = "Owner" }, Ct);
        using var demoteOwner = await tenantAdmin.PutAsJsonAsync($"/api/tenant/members/{ownerId}", new { role = "Member" }, Ct);
        using var removeOwner = await tenantAdmin.DeleteAsync(new Uri($"/api/tenant/members/{ownerId}", UriKind.Relative), Ct);
        using var inviteMember = await tenantAdmin.PostAsJsonAsync("/api/tenant/members", new { email = AuthApi.NewEmail(), role = "Member" }, Ct);

        foreach (var forbidden in new[] { inviteOwner, demoteOwner, removeOwner })
        {
            forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await forbidden.ReadAsync<ProblemResponse>()).Code.ShouldBe("Tenants.OwnerRequired");
        }

        inviteMember.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task MembersOfAnotherTenantAreNeitherListedNorReachable()
    {
        var (tenantA, ownerA, _) = await CreateTenantWithOwnerAsync();
        var (tenantB, ownerB, _) = await CreateTenantWithOwnerAsync();
        using var clientA = ownerA;
        using var clientB = ownerB;

        var membersB = await clientB.GetJsonAsync<List<TenantMemberResponse>>("/api/tenant/members");
        membersB.ShouldHaveSingleItem();

        clientB.DefaultRequestHeaders.Remove("X-Tenant");
        clientB.DefaultRequestHeaders.Add("X-Tenant", tenantA.ToString());
        using var foreign = await clientB.GetAsync(new Uri("/api/tenant/members", UriKind.Relative), Ct);
        foreign.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await foreign.ReadAsync<ProblemResponse>()).Code.ShouldBe("Tenants.Forbidden");
        tenantB.ShouldNotBe(tenantA);
    }

    [Fact]
    public async Task SuperAdminEntryIntoATenantIsRecordedOncePerWindowAndVisibleToTheOwner()
    {
        var (tenantId, owner, _) = await CreateTenantWithOwnerAsync();
        using var ownerClient = owner;
        using var admin = await factory.CreateAdminClientAsync();
        admin.DefaultRequestHeaders.Add("X-Tenant", tenantId.ToString());

        (await admin.GetAsync(new Uri("/api/tenant/members", UriKind.Relative), Ct)).EnsureSuccessStatusCode();
        (await admin.GetAsync(new Uri("/api/tenant/members", UriKind.Relative), Ct)).EnsureSuccessStatusCode();

        var entries = await ownerClient.GetJsonAsync<List<SupportAccessResponse>>("/api/tenant/support-access");
        entries.ShouldHaveSingleItem().Email.ShouldBe(SqlServerApiFactory.AdminEmail);
    }

    private static string NewSlug() => $"t-{Guid.NewGuid():N}"[..20];

    /// <summary>Creates a tenant as the super-admin with a new Owner who accepted the invitation; the client sends X-Tenant.</summary>
    private async Task<(Guid TenantId, HttpClient Owner, Guid OwnerId)> CreateTenantWithOwnerAsync()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var ownerEmail = AuthApi.NewEmail();
        using var response = await admin.PostAsJsonAsync(
            "/api/tenants", new { name = "Agency", slug = NewSlug(), ownerEmail, ownerDisplayName = "Olivia Owner" }, Ct);
        response.EnsureSuccessStatusCode();
        var created = await response.ReadAsync<CreatedTenantResponse>();

        var owner = await AcceptAsync(ownerEmail, created.Tenant.Id);
        return (created.Tenant.Id, owner, created.Owner!.Member.UserId);
    }

    /// <summary>Accepts the pending invitation of <paramref name="email"/>, logs in and selects the tenant.</summary>
    private async Task<HttpClient> AcceptAsync(string email, Guid tenantId)
    {
        var client = factory.CreateClient();
        (await client.AcceptInvitationAsync(email, factory.Emails.InvitationTokenFor(email), AuthApi.ValidPassword)).EnsureSuccessStatusCode();
        client.UseToken(await client.LoginForTokensAsync(email));
        client.DefaultRequestHeaders.Add("X-Tenant", tenantId.ToString());
        return client;
    }

    private sealed record TenantResponse(Guid Id, string Name, string Slug, bool IsDemo, DateTimeOffset CreatedAt);

    private sealed record MyTenantResponse(Guid Id, string Name, string Slug, bool IsDemo, string Role);

    private sealed record TenantMemberResponse(
        Guid UserId,
        string Email,
        string DisplayName,
        string Role,
        bool IsActive,
        bool IsInvitationPending,
        DateTimeOffset JoinedAt);

    private sealed record InvitedMemberResponse(TenantMemberResponse Member, bool AccountCreated, bool InvitationSent);

    private sealed record CreatedTenantResponse(TenantResponse Tenant, InvitedMemberResponse? Owner);

    private sealed record SupportAccessResponse(Guid Id, Guid UserId, string Email, string DisplayName, DateTimeOffset At);
}
