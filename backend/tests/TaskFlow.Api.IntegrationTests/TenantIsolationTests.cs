using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Api.IntegrationTests.Infrastructure;
using TaskFlow.Application.Notifications;
using TaskFlow.Domain.Tenancy;
using TaskFlow.Infrastructure.Tenancy;

namespace TaskFlow.Api.IntegrationTests;

/// <summary>What one tenant must never see of another (ADR 0012, 0013). Each test builds its own tenants.</summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class TenantIsolationTests(SqlServerApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ANeighborTenantsDataIsNeitherListedNorReachableById()
    {
        using var a = await NewTenantAsync();
        using var b = await NewTenantAsync();

        (await ItemIdsAsync(b.Owner, "/api/clients")).ShouldNotContain(a.ClientId);
        (await ItemIdsAsync(b.Owner, "/api/clients/options")).ShouldNotContain(a.ClientId);
        (await ItemIdsAsync(b.Owner, "/api/projects")).ShouldNotContain(a.ProjectId);
        (await ItemIdsAsync(b.Owner, "/api/notifications")).ShouldBeEmpty();
        (await b.Owner.GetJsonAsync<JsonElement>("/api/notifications/unread-count")).GetProperty("count").GetInt32().ShouldBe(0);
        (await b.Owner.GetJsonAsync<JsonElement>("/api/dashboard")).ToString().ShouldNotContain(a.ProjectId.ToString());

        var project = $"/api/projects/{a.ProjectId}";
        var task = $"{project}/tasks/{a.TaskId}";
        var comment = $"{task}/comments/{a.CommentId}";
        var entry = $"{task}/time-entries/{a.EntryId}";
        var calls = new (HttpMethod Method, string Url, object? Body)[]
        {
            (HttpMethod.Get, $"/api/clients/{a.ClientId}", null),
            (HttpMethod.Put, $"/api/clients/{a.ClientId}", new { name = "Hijacked" }),
            (HttpMethod.Post, $"/api/clients/{a.ClientId}/archive", null),
            (HttpMethod.Post, $"/api/clients/{a.ClientId}/restore", null),
            (HttpMethod.Get, project, null),
            (HttpMethod.Put, project, new { clientId = b.ClientId, name = "Hijacked", rowVersion = a.ProjectRowVersion }),
            (HttpMethod.Post, $"{project}/archive", null),
            (HttpMethod.Post, $"{project}/restore", null),
            (HttpMethod.Get, $"{project}/dashboard", null),
            (HttpMethod.Get, $"{project}/activity", null),
            (HttpMethod.Get, $"{task}/activity", null),
            (HttpMethod.Get, $"{project}/time-summary", null),
            (HttpMethod.Get, $"{project}/time-summary.csv", null),
            (HttpMethod.Get, $"{project}/tasks", null),
            (HttpMethod.Get, task, null),
            (HttpMethod.Post, $"{project}/tasks", new { title = "Planted" }),
            (HttpMethod.Put, task, new { title = "Hijacked", priority = "Low", rowVersion = a.TaskRowVersion }),
            (HttpMethod.Put, $"{task}/position", new { status = "Done", position = 0, rowVersion = a.TaskRowVersion }),
            (HttpMethod.Delete, task, null),
            (HttpMethod.Get, $"{project}/labels", null),
            (HttpMethod.Post, $"{project}/labels", new { name = "Planted", color = "#000000" }),
            (HttpMethod.Put, $"{project}/labels/{a.LabelId}", new { name = "Hijacked", color = "#000000" }),
            (HttpMethod.Delete, $"{project}/labels/{a.LabelId}", null),
            (HttpMethod.Get, $"{task}/comments", null),
            (HttpMethod.Post, $"{task}/comments", new { body = "Planted" }),
            (HttpMethod.Put, comment, new { body = "Hijacked" }),
            (HttpMethod.Delete, comment, null),
            (HttpMethod.Get, $"{task}/time-entries", null),
            (HttpMethod.Post, $"{task}/time-entries", new { date = factory.Today, minutes = 5 }),
            (HttpMethod.Put, entry, new { date = factory.Today, minutes = 5 }),
            (HttpMethod.Delete, entry, null),
            (HttpMethod.Post, $"/api/notifications/{a.NotificationId}/read", null),
        };

        foreach (var (method, url, body) in calls)
        {
            using var request = new HttpRequestMessage(method, url);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            using var response = await b.Owner.SendAsync(request, Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound, $"{method} {url}");
        }

        // None of the above changed A's data.
        (await a.Owner.GetJsonAsync<JsonElement>(task)).GetProperty("title").GetString().ShouldBe("Task of A");
        (await a.Owner.GetJsonAsync<JsonElement>($"/api/clients/{a.ClientId}")).GetProperty("isArchived").GetBoolean().ShouldBeFalse();
        (await ItemIdsAsync(a.Member, "/api/notifications")).ShouldContain(a.NotificationId);
    }

    [Fact]
    public async Task NamingAnotherTenantInTheHeaderIsForbidden()
    {
        using var a = await NewTenantAsync();
        using var b = await NewTenantAsync();
        using var intruder = WithTenant(b.Owner, a.TenantId);

        foreach (var url in new[] { "/api/clients", "/api/projects", $"/api/projects/{a.ProjectId}", "/api/notifications", "/api/dashboard" })
        {
            using var response = await intruder.GetAsync(new Uri(url, UriKind.Relative), Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, url);
            (await response.ReadAsync<ProblemResponse>()).Code.ShouldBe("Tenants.Forbidden");
        }
    }

    [Fact]
    public async Task TheSameClientNameCanExistInTwoTenantsButNotTwiceInOne()
    {
        using var a = await NewTenantAsync();
        using var b = await NewTenantAsync();
        const string name = "Shared Client Name";

        (await a.Owner.PostAsJsonAsync("/api/clients", new { name }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await b.Owner.PostAsJsonAsync("/api/clients", new { name }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        using var twice = await a.Owner.PostAsJsonAsync("/api/clients", new { name }, Ct);

        twice.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task AUserInTwoTenantsSeesTheOneNamedInTheHeaderAndNothingWithoutIt()
    {
        using var a = await NewTenantAsync();
        using var b = await NewTenantAsync();
        var (both, email) = await factory.CreateUserClientAsync(inDefaultTenant: false);
        using var bothClient = both;
        await factory.AddToTenantAsync(email, a.TenantId);
        await factory.AddToTenantAsync(email, b.TenantId);
        (await a.Owner.AddMemberAsync(a.ProjectId, email)).EnsureSuccessStatusCode();
        (await b.Owner.AddMemberAsync(b.ProjectId, email)).EnsureSuccessStatusCode();

        var mine = await bothClient.GetJsonAsync<List<MyTenant>>("/api/tenants/mine");
        mine.Select(tenant => tenant.Id).ShouldBe([a.TenantId, b.TenantId], ignoreOrder: true);

        using var inA = WithTenant(bothClient, a.TenantId);
        using var inB = WithTenant(bothClient, b.TenantId);
        (await ItemIdsAsync(inA, "/api/projects")).ShouldBe([a.ProjectId]);
        (await ItemIdsAsync(inB, "/api/projects")).ShouldBe([b.ProjectId]);

        // Two memberships and no header: there is no default, so the data access fails closed.
        (await ItemIdsAsync(bothClient, "/api/projects")).ShouldBeEmpty();
        using var clients = await bothClient.GetAsync(new Uri("/api/clients", UriKind.Relative), Ct);
        clients.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AUserWithoutAMembershipSeesNoTenantsAndNoData()
    {
        var (lonely, _) = await factory.CreateUserClientAsync(inDefaultTenant: false);
        using var client = lonely;

        (await client.GetJsonAsync<List<MyTenant>>("/api/tenants/mine")).ShouldBeEmpty();
        (await ItemIdsAsync(client, "/api/projects")).ShouldBeEmpty();
    }

    [Fact]
    public async Task ASuperAdminSeesATenantOnlyByNamingItAndLeavesATraceTheOwnerCanRead()
    {
        using var b = await NewTenantAsync();
        using var admin = await factory.CreateAdminClientAsync();

        // Without the header the super-admin works in their own tenant, never in B.
        (await ItemIdsAsync(admin, "/api/projects")).ShouldNotContain(b.ProjectId);
        (await ItemIdsAsync(admin, "/api/clients")).ShouldNotContain(b.ClientId);

        using var inB = WithTenant(admin, b.TenantId);
        (await ItemIdsAsync(inB, "/api/projects")).ShouldContain(b.ProjectId);
        (await ItemIdsAsync(inB, "/api/clients")).ShouldContain(b.ClientId);

        var trace = await b.Owner.GetJsonAsync<List<JsonElement>>("/api/tenant/support-access");
        trace.ShouldHaveSingleItem().GetProperty("email").GetString().ShouldBe(SqlServerApiFactory.AdminEmail);
    }

    [Fact]
    public async Task RemovingAMembershipTakesEffectAtOnce()
    {
        using var a = await NewTenantAsync();
        (await ItemIdsAsync(a.Member, "/api/projects")).ShouldContain(a.ProjectId);

        using var remove = await a.Owner.DeleteAsync(new Uri($"/api/tenant/members/{a.MemberId}", UriKind.Relative), Ct);
        remove.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var after = await a.Member.GetAsync(new Uri("/api/projects", UriKind.Relative), Ct);
        after.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await after.ReadAsync<ProblemResponse>()).Code.ShouldBe("Tenants.Forbidden");
    }

    [Fact]
    public async Task TheDueSoonJobRunsPerTenantAndNeverCrossesNotifications()
    {
        using var a = await NewTenantAsync();
        using var b = await NewTenantAsync();

        (await RunDueSoonAsync(a.TenantId)).ShouldBe(1);
        (await DueSoonAsync(a.Member)).ShouldBe(1);
        (await DueSoonAsync(b.Member)).ShouldBe(0);

        (await RunDueSoonAsync(b.TenantId)).ShouldBe(1);
        (await DueSoonAsync(a.Member)).ShouldBe(1);
        (await DueSoonAsync(b.Member)).ShouldBe(1);
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

    private static async Task<int> DueSoonAsync(HttpClient client)
    {
        var page = await client.GetJsonAsync<JsonElement>("/api/notifications");
        return page.GetProperty("items").EnumerateArray().Count(item => item.GetProperty("type").GetString() == "TaskDueSoon");
    }

    /// <summary>The ids of a list endpoint, whether it answers an array or a page with <c>items</c>.</summary>
    private static async Task<List<Guid>> ItemIdsAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(new Uri(url, UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, url);
        var body = await response.ReadAsync<JsonElement>();
        var items = body.ValueKind == JsonValueKind.Array ? body : body.GetProperty("items");
        return [.. items.EnumerateArray().Select(item => item.GetProperty("id").GetGuid())];
    }

    /// <summary>A new client with the same token that sends <c>X-Tenant: tenantId</c>.</summary>
    private HttpClient WithTenant(HttpClient source, Guid tenantId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = source.DefaultRequestHeaders.Authorization;
        client.DefaultRequestHeaders.Add("X-Tenant", tenantId.ToString());
        return client;
    }

    private static string NewSlug() => $"t-{Guid.NewGuid():N}"[..20];

    /// <summary>
    /// A tenant made by the super-admin with an Owner and a Member (both sending <c>X-Tenant</c>), and in it a client,
    /// project, task (assigned to the Member, due today), label, comment, time entry and a notification for the Member.
    /// </summary>
    private async Task<TenantData> NewTenantAsync()
    {
        using var admin = await factory.CreateAdminClientAsync();
        using var created = await admin.PostAsJsonAsync("/api/tenants", new { name = "Agency", slug = NewSlug() }, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var tenantId = (await created.ReadAsync<JsonElement>()).GetProperty("tenant").GetProperty("id").GetGuid();

        var (ownerClient, ownerEmail) = await factory.CreateUserClientAsync(inDefaultTenant: false);
        var (memberClient, memberEmail) = await factory.CreateUserClientAsync(inDefaultTenant: false);
        await factory.AddToTenantAsync(ownerEmail, tenantId, TenantRole.Owner);
        await factory.AddToTenantAsync(memberEmail, tenantId);
        ownerClient.DefaultRequestHeaders.Add("X-Tenant", tenantId.ToString());
        memberClient.DefaultRequestHeaders.Add("X-Tenant", tenantId.ToString());
        var memberId = await memberClient.GetMyIdAsync();

        var client = await ownerClient.CreateClientAsync();
        var project = await ownerClient.CreateProjectAsync(client.Id);
        (await ownerClient.AddMemberAsync(project.Id, memberEmail)).EnsureSuccessStatusCode();
        var task = await ownerClient.CreateTaskAsync(
            project.Id, new { title = "Task of A", assigneeId = memberId, dueDate = factory.Today });

        using var label = await ownerClient.PostAsJsonAsync($"/api/projects/{project.Id}/labels", new { name = "Bug", color = "#FF0000" }, Ct);
        label.EnsureSuccessStatusCode();
        var taskUrl = $"/api/projects/{project.Id}/tasks/{task.Id}";
        using var comment = await ownerClient.PostAsJsonAsync($"{taskUrl}/comments", new { body = "Hello" }, Ct);
        comment.EnsureSuccessStatusCode();
        using var entry = await memberClient.PostAsJsonAsync($"{taskUrl}/time-entries", new { date = factory.Today, minutes = 30 }, Ct);
        entry.EnsureSuccessStatusCode();
        var notification = (await ItemIdsAsync(memberClient, "/api/notifications")).First();

        return new TenantData(
            tenantId, ownerClient, memberClient, memberId, client.Id, project.Id, project.RowVersion, task.Id, task.RowVersion,
            (await label.ReadAsync<JsonElement>()).GetProperty("id").GetGuid(),
            (await comment.ReadAsync<JsonElement>()).GetProperty("id").GetGuid(),
            (await entry.ReadAsync<JsonElement>()).GetProperty("id").GetGuid(),
            notification);
    }

    private sealed record MyTenant(Guid Id, string Name);

    private sealed record TenantData(
        Guid TenantId,
        HttpClient Owner,
        HttpClient Member,
        Guid MemberId,
        Guid ClientId,
        Guid ProjectId,
        byte[] ProjectRowVersion,
        Guid TaskId,
        byte[] TaskRowVersion,
        Guid LabelId,
        Guid CommentId,
        Guid EntryId,
        Guid NotificationId) : IDisposable
    {
        public void Dispose()
        {
            Owner.Dispose();
            Member.Dispose();
        }
    }
}
