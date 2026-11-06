using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Api.IntegrationTests.Infrastructure;
using TaskFlow.Domain.Tenancy;
using TaskFlow.Infrastructure.Identity;
using TaskFlow.Infrastructure.Tenancy;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Api.IntegrationTests;

/// <summary>API keys end to end (ADR 0016, 0017): the lifecycle, scopes, tenant binding, the owner and the rate limit.</summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class ApiKeyTests(SqlServerApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheSecretIsShownOnceAndTheKeyReadsAProject()
    {
        using var owner = await NewOwnerAsync();
        var project = await factory.CreateProjectAsync(owner.Client);

        var created = await owner.CreateKeyAsync("Reader", "Read");
        created.Secret.ShouldStartWith("tf_");
        created.Prefix.ShouldBe(created.Secret[..11]);

        using var listed = await owner.Client.GetAsync(new Uri("/api/api-keys", UriKind.Relative), Ct);
        var body = await listed.Content.ReadAsStringAsync(Ct);
        body.ShouldNotContain(created.Secret);
        var keys = JsonSerializer.Deserialize<List<KeyResponse>>(body, ApiJson.Options)!;
        keys.Single(key => key.Id == created.Id).IsActive.ShouldBeTrue();

        using var viaKey = owner.UseKey(created.Secret);
        var read = await viaKey.GetJsonAsync<ProjectResponse>($"/api/projects/{project.Id}");
        read.Id.ShouldBe(project.Id);
    }

    [Fact]
    public async Task ARevokedKeyAnswers401()
    {
        using var owner = await NewOwnerAsync();
        var project = await factory.CreateProjectAsync(owner.Client);
        var created = await owner.CreateKeyAsync("Short lived", "Read");
        using var viaKey = owner.UseKey(created.Secret);
        (await viaKey.GetAsync(new Uri($"/api/projects/{project.Id}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var revoke = await owner.Client.DeleteAsync(new Uri($"/api/api-keys/{created.Id}", UriKind.Relative), Ct);
        revoke.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await viaKey.GetAsync(new Uri($"/api/projects/{project.Id}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnExpiredKeyAnswers401()
    {
        using var owner = await NewOwnerAsync();
        var project = await factory.CreateProjectAsync(owner.Client);
        var created = await owner.CreateKeyAsync("Expires soon", "Read", factory.Time.GetUtcNow().AddMinutes(10));
        using var viaKey = owner.UseKey(created.Secret);
        (await viaKey.GetAsync(new Uri($"/api/projects/{project.Id}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        factory.Time.Advance(TimeSpan.FromMinutes(11));

        (await viaKey.GetAsync(new Uri($"/api/projects/{project.Id}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AReadKeyCannotWriteButAWriteKeyMovesTasks()
    {
        using var owner = await NewOwnerAsync();
        var project = await factory.CreateProjectAsync(owner.Client);
        var task = await owner.Client.CreateTaskAsync(project.Id, new { title = "Move me" });

        using var reader = owner.UseKey((await owner.CreateKeyAsync("Reader", "Read")).Secret);
        using var create = await reader.PostAsJsonAsync($"/api/projects/{project.Id}/tasks", new { title = "Nope" }, Ct);
        create.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await create.ReadAsync<ProblemResponse>()).Code.ShouldBe("ApiKeys.ScopeInsufficient");
        using var move = await reader.TryMoveTaskAsync(project.Id, task, "InProgress", 0);
        move.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var writer = owner.UseKey((await owner.CreateKeyAsync("Writer", "Write")).Secret);
        var moved = await writer.MoveTaskAsync(project.Id, task, "InProgress", 0);
        moved.Status.ShouldBe("InProgress");
    }

    [Fact]
    public async Task ManageActionsAreDeniedToEvenAWriteKey()
    {
        using var owner = await NewOwnerAsync();
        var project = await factory.CreateProjectAsync(owner.Client);
        var (teammateClient, teammate) = await factory.CreateUserClientAsync();
        teammateClient.Dispose();
        using var writer = owner.UseKey((await owner.CreateKeyAsync("Writer", "Write")).Secret);
        var url = $"/api/projects/{project.Id}";

        var calls = new (HttpMethod Method, string Url, object? Body)[]
        {
            (HttpMethod.Put, url, new { clientId = project.ClientId, name = "Renamed", rowVersion = project.RowVersion }),
            (HttpMethod.Post, $"{url}/archive", null),
            (HttpMethod.Post, $"{url}/restore", null),
            (HttpMethod.Post, $"{url}/members", new { email = teammate, role = "Member" }),
            (HttpMethod.Post, "/api/projects", new { clientId = project.ClientId, name = "Created by a key" }),
        };
        foreach (var (method, path, body) in calls)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            using var response = await writer.SendAsync(request, Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{method} {path}");
        }

        // Nothing changed.
        var after = await owner.Client.GetJsonAsync<ProjectResponse>(url);
        after.Name.ShouldBe(project.Name);
        after.IsArchived.ShouldBeFalse();
    }

    [Fact]
    public async Task AKeyCannotManageKeysOrReachOutsideTheProjectsApi()
    {
        using var owner = await NewOwnerAsync(TenantRole.Owner);
        var created = await owner.CreateKeyAsync("Writer", "Write");
        using var writer = owner.UseKey(created.Secret);

        using var newKey = await writer.PostAsJsonAsync("/api/api-keys", new { name = "Child", scope = "Write" }, Ct);
        newKey.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var list = await writer.GetAsync(new Uri("/api/api-keys", UriKind.Relative), Ct);
        list.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var revoke = await writer.DeleteAsync(new Uri($"/api/api-keys/{created.Id}", UriKind.Relative), Ct);
        revoke.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The owner is a tenant Owner, yet the key gets no TenantAdmin.
        using var members = await writer.GetAsync(new Uri("/api/tenant/members", UriKind.Relative), Ct);
        members.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var client = await writer.PostAsJsonAsync("/api/clients", new { name = $"Client {Guid.NewGuid():N}" }, Ct);
        client.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var users = await writer.GetAsync(new Uri("/api/users/me", UriKind.Relative), Ct);
        users.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The same person with a normal login can.
        using var asPerson = await owner.Client.GetAsync(new Uri("/api/tenant/members", UriKind.Relative), Ct);
        asPerson.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.Client.GetAsync(new Uri("/api/api-keys", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AKeyIsBoundToItsTenantAndIgnoresTheTenantHeader()
    {
        using var admin = await factory.CreateAdminClientAsync();
        using var createdTenant = await admin.PostAsJsonAsync("/api/tenants", new { name = "Other agency", slug = $"t-{Guid.NewGuid():N}"[..20] }, Ct);
        createdTenant.StatusCode.ShouldBe(HttpStatusCode.Created);
        var otherTenant = (await createdTenant.ReadAsync<JsonElement>()).GetProperty("tenant").GetProperty("id").GetGuid();

        // One person in both tenants, so the header means something for them.
        var (person, email) = await factory.CreateUserClientAsync();
        using var personClient = person;
        await factory.AddToTenantAsync(email, otherTenant, TenantRole.Owner);

        var home = TenantDefaults.DefaultId.ToString();
        personClient.DefaultRequestHeaders.Add("X-Tenant", home);
        var homeProject = await factory.CreateProjectAsync(personClient);
        personClient.DefaultRequestHeaders.Remove("X-Tenant");
        personClient.DefaultRequestHeaders.Add("X-Tenant", otherTenant.ToString());
        var otherClient = await personClient.CreateClientAsync();
        var otherProject = await personClient.CreateProjectAsync(otherClient.Id);

        // Created while working in the home tenant: that is the key's tenant.
        personClient.DefaultRequestHeaders.Remove("X-Tenant");
        personClient.DefaultRequestHeaders.Add("X-Tenant", home);
        using var created = await personClient.PostAsJsonAsync("/api/api-keys", new { name = "Home", scope = "Write" }, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var key = await created.ReadAsync<CreatedKeyResponse>();

        using var viaKey = factory.CreateClient();
        viaKey.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key.Secret);
        foreach (var header in new string?[] { null, otherTenant.ToString(), Guid.NewGuid().ToString() })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/projects?pageSize=100");
            if (header is not null)
            {
                request.Headers.Add("X-Tenant", header);
            }

            using var response = await viaKey.SendAsync(request, Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, $"X-Tenant: {header}");
            var ids = (await response.ReadAsync<JsonElement>()).GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("id").GetGuid()).ToList();
            ids.ShouldContain(homeProject.Id);
            ids.ShouldNotContain(otherProject.Id);
        }

        using var direct = await viaKey.GetAsync(new Uri($"/api/projects/{otherProject.Id}", UriKind.Relative), Ct);
        direct.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ADeactivatedOwnersKeyStopsWorkingAtOnce()
    {
        using var owner = await NewOwnerAsync();
        var project = await factory.CreateProjectAsync(owner.Client);
        using var viaKey = owner.UseKey((await owner.CreateKeyAsync("Reader", "Read")).Secret);
        var url = new Uri($"/api/projects/{project.Id}", UriKind.Relative);

        // Used once, so the owner's state is cached; deactivating must not wait for the cache to expire.
        (await viaKey.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var admin = await factory.CreateAdminClientAsync();
        (await admin.DeactivateUserAsync(await owner.Client.GetMyIdAsync())).EnsureSuccessStatusCode();

        (await viaKey.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        factory.Time.Advance(TimeSpan.FromSeconds(31));
        (await viaKey.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RenewingTheOwnersSecurityStampKillsTheBearerButNotTheKey()
    {
        using var owner = await NewOwnerAsync();
        var project = await factory.CreateProjectAsync(owner.Client);
        using var viaKey = owner.UseKey((await owner.CreateKeyAsync("Reader", "Read")).Secret);
        var url = new Uri($"/api/projects/{project.Id}", UriKind.Relative);
        (await owner.Client.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await viaKey.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync(owner.Email))!;
            (await users.UpdateSecurityStampAsync(user)).Succeeded.ShouldBeTrue();
            scope.ServiceProvider.GetRequiredService<SessionValidator>().Forget(user.Id);
        }

        (await owner.Client.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await viaKey.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task EachKeyHasItsOwnRateLimit()
    {
        using var owner = await NewOwnerAsync();
        var project = await factory.CreateProjectAsync(owner.Client);
        var first = await owner.CreateKeyAsync("First", "Read");
        var second = await owner.CreateKeyAsync("Second", "Read");

        // A derived host with a tiny budget; it shares the database and the clock with the rest of the suite.
        await using var limited = factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:ApiKey:PermitLimit", "3"));
        using var firstClient = limited.CreateClient();
        firstClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.Secret);
        using var secondClient = limited.CreateClient();
        secondClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", second.Secret);
        var url = new Uri($"/api/projects/{project.Id}", UriKind.Relative);

        for (var i = 0; i < 3; i++)
        {
            (await firstClient.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await firstClient.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await secondClient.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PutByKeyIsIdempotent()
    {
        using var owner = await NewOwnerAsync();
        var project = await factory.CreateProjectAsync(owner.Client);
        using var writer = owner.UseKey((await owner.CreateKeyAsync("Writer", "Write")).Secret);
        var body = new { title = "Design the logo", description = "v1", startDate = factory.Today, dueDate = factory.Today.AddDays(3), isBlocked = true, statusNote = "waiting" };
        var url = $"/api/projects/{project.Id}/tasks/by-key/T-01";

        using var first = await writer.PutAsJsonAsync(url, body, Ct);
        using var second = await writer.PutAsJsonAsync(url, body, Ct);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        var one = await first.ReadAsync<PlannedTask>();
        var two = await second.ReadAsync<PlannedTask>();
        two.Id.ShouldBe(one.Id);
        two.ExternalKey.ShouldBe("T-01");
        two.IsBlocked.ShouldBeTrue();
        two.StatusNote.ShouldBe("waiting");
        two.StartDate.ShouldBe(factory.Today);
        (await ListAsync(owner.Client, project.Id)).Count(task => task.ExternalKey == "T-01").ShouldBe(1);
    }

    [Fact]
    public async Task SyncAcceptsDependenciesInAnyOrder()
    {
        using var owner = await NewOwnerAsync();
        var project = await factory.CreateProjectAsync(owner.Client);
        using var writer = owner.UseKey((await owner.CreateKeyAsync("Writer", "Write")).Secret);

        // C depends on B which depends on A, listed in reverse.
        var items = new object[]
        {
            Item("C", dependsOn: DependsOnB),
            Item("B", dependsOn: DependsOnA),
            Item("A"),
        };
        using var response = await writer.PostAsJsonAsync($"/api/projects/{project.Id}/tasks/sync", new { items }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.ReadAsync<SyncResponse>();
        result.Applied.ShouldBeTrue();
        result.Items.ShouldAllBe(item => item.Outcome == "Created");
        var tasks = await ListAsync(owner.Client, project.Id);
        var byKey = tasks.ToDictionary(task => task.ExternalKey!);
        byKey["C"].DependsOn.ShouldBe([byKey["B"].Id]);
        byKey["B"].DependsOn.ShouldBe([byKey["A"].Id]);
        byKey["A"].DependsOn.ShouldBeEmpty();

        // Running it again updates in place.
        using var again = await writer.PostAsJsonAsync($"/api/projects/{project.Id}/tasks/sync", new { items }, Ct);
        (await again.ReadAsync<SyncResponse>()).Items.ShouldAllBe(item => item.Outcome == "Updated");
        (await ListAsync(owner.Client, project.Id)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task ASyncWithACycleAnswers422AndSavesNothing()
    {
        using var owner = await NewOwnerAsync();
        var project = await factory.CreateProjectAsync(owner.Client);
        using var writer = owner.UseKey((await owner.CreateKeyAsync("Writer", "Write")).Secret);
        var items = new object[]
        {
            Item("X"),
            Item("A", dependsOn: DependsOnB),
            Item("B", dependsOn: DependsOnA),
        };

        using var response = await writer.PostAsJsonAsync($"/api/projects/{project.Id}/tasks/sync", new { items }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var result = await response.ReadAsync<SyncResponse>();
        result.Applied.ShouldBeFalse();
        result.Items.Single(item => item.ExternalKey == "X").Outcome.ShouldBe("NotApplied");
        result.Items.Where(item => item.Outcome == "Failed").ShouldNotBeEmpty();
        result.Items.Where(item => item.Outcome == "Failed").ShouldAllBe(item => item.ErrorCode == "Tasks.DependencyCycle");
        (await ListAsync(owner.Client, project.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ASyncWithADuplicateKeyAnswers422AndSavesNothing()
    {
        using var owner = await NewOwnerAsync();
        var project = await factory.CreateProjectAsync(owner.Client);
        using var writer = owner.UseKey((await owner.CreateKeyAsync("Writer", "Write")).Secret);

        using var response = await writer.PostAsJsonAsync(
            $"/api/projects/{project.Id}/tasks/sync", new { items = new object[] { Item("D1"), Item("D2"), Item("D1") } }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var result = await response.ReadAsync<SyncResponse>();
        var failed = result.Items.Single(item => item.Outcome == "Failed");
        failed.ErrorCode.ShouldBe("Tasks.DuplicateExternalKey");
        (await ListAsync(owner.Client, project.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ASyncTakesAtMost200Tasks()
    {
        using var owner = await NewOwnerAsync();
        var project = await factory.CreateProjectAsync(owner.Client);
        using var writer = owner.UseKey((await owner.CreateKeyAsync("Writer", "Write")).Secret);
        var url = $"/api/projects/{project.Id}/tasks/sync";

        using var tooMany = await writer.PostAsJsonAsync(url, new { items = Enumerable.Range(1, 201).Select(i => Item($"K{i}")).ToArray() }, Ct);
        tooMany.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await tooMany.ReadAsync<ProblemResponse>()).Code.ShouldBe("Tasks.SyncTooLarge");
        (await ListAsync(owner.Client, project.Id)).ShouldBeEmpty();

        using var exactly = await writer.PostAsJsonAsync(url, new { items = Enumerable.Range(1, 200).Select(i => Item($"K{i}")).ToArray() }, Ct);
        exactly.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ListAsync(owner.Client, project.Id)).Count.ShouldBe(200);

        using var empty = await writer.PostAsJsonAsync(url, new { items = Array.Empty<object>() }, Ct);
        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ActivityMadeThroughAKeyRecordsTheKeyAndPeopleDoNot()
    {
        using var owner = await NewOwnerAsync();
        var project = await factory.CreateProjectAsync(owner.Client);
        var created = await owner.CreateKeyAsync("Writer", "Write");
        using var writer = owner.UseKey(created.Secret);

        using var viaKey = await writer.PutAsJsonAsync($"/api/projects/{project.Id}/tasks/by-key/ACT-1", new { title = "By a key" }, Ct);
        viaKey.StatusCode.ShouldBe(HttpStatusCode.Created);
        var byPerson = await owner.Client.CreateTaskAsync(project.Id, new { title = "By a person" });

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entries = await db.ActivityEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(entry => entry.ProjectId == project.Id)
            .Select(entry => new { TaskId = entry.TaskItemId, entry.ApiKeyId })
            .ToListAsync(Ct);
        var keyTaskId = (await viaKey.ReadAsync<PlannedTask>()).Id;
        entries.Where(entry => entry.TaskId == keyTaskId).ShouldAllBe(entry => entry.ApiKeyId == created.Id);
        entries.Where(entry => entry.TaskId == keyTaskId).ShouldNotBeEmpty();
        entries.Where(entry => entry.TaskId == byPerson.Id).ShouldAllBe(entry => entry.ApiKeyId == null);
    }

    [Fact]
    public async Task EditingATaskSetsTheStartDateTheBlockAndTheNote()
    {
        using var owner = await NewOwnerAsync();
        var project = await factory.CreateProjectAsync(owner.Client);
        var task = await owner.Client.CreateTaskAsync(project.Id, new { title = "Plan me" });
        var uri = $"/api/projects/{project.Id}/tasks/{task.Id}";

        using var edit = await owner.Client.PutAsJsonAsync(
            uri,
            new { title = "Plan me", priority = "Low", task.RowVersion, startDate = factory.Today, dueDate = factory.Today.AddDays(2), isBlocked = true, statusNote = "  waiting for the client " },
            Ct);

        edit.StatusCode.ShouldBe(HttpStatusCode.OK);
        var edited = await edit.ReadAsync<EditedTask>();
        edited.StartDate.ShouldBe(factory.Today);
        edited.IsBlocked.ShouldBeTrue();
        edited.StatusNote.ShouldBe("waiting for the client");

        // A start date after the due date and a note over 500 characters are rejected.
        using var inverted = await owner.Client.PutAsJsonAsync(
            uri, new { title = "Plan me", priority = "Low", edited.RowVersion, startDate = factory.Today.AddDays(5), dueDate = factory.Today.AddDays(2) }, Ct);
        inverted.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await inverted.ReadAsync<ProblemResponse>()).Code.ShouldBe("Tasks.StartAfterDue");
        using var longNote = await owner.Client.PutAsJsonAsync(
            uri, new { title = "Plan me", priority = "Low", edited.RowVersion, statusNote = new string('x', 501) }, Ct);
        longNote.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await longNote.ReadAsync<ProblemResponse>()).Errors!.ShouldContainKey("statusNote");

        // Leaving the fields out clears them.
        using var cleared = await owner.Client.PutAsJsonAsync(uri, new { title = "Plan me", priority = "Low", edited.RowVersion }, Ct);
        var after = await cleared.ReadAsync<EditedTask>();
        after.StartDate.ShouldBeNull();
        after.IsBlocked.ShouldBeFalse();
        after.StatusNote.ShouldBeNull();
    }

    [Fact]
    public async Task TheActivityListSaysWhichEntriesCameThroughAKey()
    {
        using var owner = await NewOwnerAsync();
        var project = await factory.CreateProjectAsync(owner.Client);
        using var writer = owner.UseKey((await owner.CreateKeyAsync("Writer", "Write")).Secret);

        using var viaKey = await writer.PutAsJsonAsync($"/api/projects/{project.Id}/tasks/by-key/VIA-1", new { title = "By a key" }, Ct);
        var keyTask = await viaKey.ReadAsync<PlannedTask>();
        var byPerson = await owner.Client.CreateTaskAsync(project.Id, new { title = "By a person" });

        var page = await owner.Client.GetJsonAsync<ActivityPage>($"/api/projects/{project.Id}/activity");
        page.Items.Single(entry => entry.TaskId == keyTask.Id).ViaApiKey.ShouldBeTrue();
        page.Items.Single(entry => entry.TaskId == byPerson.Id).ViaApiKey.ShouldBeFalse();
        var raw = await owner.Client.GetStringAsync(new Uri($"/api/projects/{project.Id}/activity", UriKind.Relative), Ct);
        raw.ShouldNotContain("apiKeyId", Case.Insensitive);

        var forTask = await owner.Client.GetJsonAsync<ActivityPage>($"/api/projects/{project.Id}/tasks/{keyTask.Id}/activity");
        forTask.Items.ShouldAllBe(entry => entry.ViaApiKey);
    }

    [Fact]
    public async Task AnUpsertReplacesLabelsKeepsThemWhenOmittedAndCreatesMissingOnesForAPerson()
    {
        using var owner = await NewOwnerAsync();
        var project = await factory.CreateProjectAsync(owner.Client);
        var url = $"/api/projects/{project.Id}/tasks/by-key/L-1";

        // A person who owns the project may create the labels.
        using var first = await owner.Client.PutAsJsonAsync(url, new { title = "Labelled", labels = BackendAndUrgent }, Ct);
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await first.ReadAsync<PlannedTask>()).Labels.Select(label => label.Name).Order().ShouldBe(["agent:backend", "Urgent"]);

        // Omitted: kept. An existing name in another case reuses the label instead of creating a second one.
        using var kept = await owner.Client.PutAsJsonAsync(url, new { title = "Labelled again" }, Ct);
        (await kept.ReadAsync<PlannedTask>()).Labels.Count.ShouldBe(2);
        using var replaced = await owner.Client.PutAsJsonAsync(url, new { title = "Labelled again", labels = BackendUpper }, Ct);
        (await replaced.ReadAsync<PlannedTask>()).Labels.Select(label => label.Name).ShouldBe(["agent:backend"]);
        using var emptied = await owner.Client.PutAsJsonAsync(url, new { title = "Labelled again", labels = NoLabels }, Ct);
        (await emptied.ReadAsync<PlannedTask>()).Labels.ShouldBeEmpty();
        (await owner.Client.GetJsonAsync<List<NamedLabel>>($"/api/projects/{project.Id}/labels")).Count.ShouldBe(2);
    }

    [Fact]
    public async Task AKeyCanUseExistingLabelsButCannotCreateOnesInUpsertAndSync()
    {
        using var owner = await NewOwnerAsync();
        var project = await factory.CreateProjectAsync(owner.Client);
        using var made = await owner.Client.PostAsJsonAsync($"/api/projects/{project.Id}/labels", new { name = "agent:gantt", color = "#112233" }, Ct);
        made.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var writer = owner.UseKey((await owner.CreateKeyAsync("Writer", "Write")).Secret);

        using var allowed = await writer.PutAsJsonAsync(
            $"/api/projects/{project.Id}/tasks/by-key/K-1", new { title = "Existing label", labels = GanttLabel }, Ct);
        allowed.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await allowed.ReadAsync<PlannedTask>()).Labels.Select(label => label.Name).ShouldBe(["agent:gantt"]);

        using var denied = await writer.PutAsJsonAsync(
            $"/api/projects/{project.Id}/tasks/by-key/K-2", new { title = "New label", labels = NewLabel }, Ct);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await denied.ReadAsync<ProblemResponse>()).Code.ShouldBe("Labels.CreateNotAllowed");

        // In a sync the item fails, the others are not applied, and the project is left as it was.
        var items = new object[]
        {
            new { externalKey = "S-1", title = "Fine", labels = GanttLabel },
            new { externalKey = "S-2", title = "Bad", labels = NewLabel },
        };
        using var sync = await writer.PostAsJsonAsync($"/api/projects/{project.Id}/tasks/sync", new { items }, Ct);
        sync.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var result = await sync.ReadAsync<SyncResponse>();
        result.Items.Single(item => item.ExternalKey == "S-2").ErrorCode.ShouldBe("Labels.CreateNotAllowed");
        result.Items.Single(item => item.ExternalKey == "S-1").Outcome.ShouldBe("NotApplied");
        (await ListAsync(owner.Client, project.Id)).Select(task => task.ExternalKey).ShouldBe(["K-1"]);

        // With only existing labels, the sync works and sets them.
        var good = new object[] { new { externalKey = "S-1", title = "Fine", labels = GanttLabel } };
        using var ok = await writer.PostAsJsonAsync($"/api/projects/{project.Id}/tasks/sync", new { items = good }, Ct);
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ListAsync(owner.Client, project.Id)).Single(task => task.ExternalKey == "S-1").Labels.Select(label => label.Name).ShouldBe(["agent:gantt"]);
    }

    [Fact]
    public async Task ASyncByAPersonCreatesALabelOnceForSeveralTasks()
    {
        using var owner = await NewOwnerAsync();
        var project = await factory.CreateProjectAsync(owner.Client);
        var items = new object[]
        {
            new { externalKey = "P-1", title = "One", labels = FrontendLabel },
            new { externalKey = "P-2", title = "Two", labels = FrontendLabel },
        };

        using var sync = await owner.Client.PostAsJsonAsync($"/api/projects/{project.Id}/tasks/sync", new { items }, Ct);

        sync.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.Client.GetJsonAsync<List<NamedLabel>>($"/api/projects/{project.Id}/labels")).Count.ShouldBe(1);
        (await ListAsync(owner.Client, project.Id)).ShouldAllBe(task => task.Labels.Count == 1);
    }

    private static readonly string[] BackendAndUrgent = ["agent:backend", "Urgent"];
    private static readonly string[] BackendUpper = ["AGENT:BACKEND"];
    private static readonly string[] GanttLabel = ["agent:gantt"];
    private static readonly string[] NewLabel = ["agent:new"];
    private static readonly string[] FrontendLabel = ["agent:frontend"];
    private static readonly string[] NoLabels = [];
    private static readonly string[] DependsOnA = ["A"];
    private static readonly string[] DependsOnB = ["B"];

    private static object Item(string key, string[]? dependsOn = null) =>
        dependsOn is null
            ? new { externalKey = key, title = $"Task {key}" }
            : new { externalKey = key, title = $"Task {key}", dependsOn };

    private static async Task<List<PlannedTask>> ListAsync(HttpClient client, Guid projectId) =>
        await client.GetJsonAsync<List<PlannedTask>>($"/api/projects/{projectId}/tasks");

    /// <summary>A member of the default tenant (or with <paramref name="role"/> there) with a normal login.</summary>
    private async Task<KeyOwner> NewOwnerAsync(TenantRole role = TenantRole.Member)
    {
        var (client, email) = await factory.CreateUserClientAsync(inDefaultTenant: false);
        await factory.AddToTenantAsync(email, TenantDefaults.DefaultId, role);
        return new KeyOwner(factory, client, email);
    }

    private sealed class KeyOwner(SqlServerApiFactory factory, HttpClient client, string email) : IDisposable
    {
        public HttpClient Client { get; } = client;

        public string Email { get; } = email;

        public async Task<CreatedKeyResponse> CreateKeyAsync(string name, string scope, DateTimeOffset? expiresAt = null)
        {
            using var response = await Client.PostAsJsonAsync("/api/api-keys", new { name, scope, expiresAt }, Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            return await response.ReadAsync<CreatedKeyResponse>();
        }

        /// <summary>A client that authenticates with the key and nothing else.</summary>
        public HttpClient UseKey(string secret)
        {
            var viaKey = factory.CreateClient();
            viaKey.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
            return viaKey;
        }

        public void Dispose() => Client.Dispose();
    }

    private sealed record CreatedKeyResponse(Guid Id, string Name, string Prefix, string Scope, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, string Secret);

    private sealed record KeyResponse(
        Guid Id, string Name, string Prefix, string Scope, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
        DateTimeOffset? RevokedAt, DateTimeOffset? LastUsedAt, bool IsActive);

    private sealed record PlannedTask(
        Guid Id, string Title, string Status, string? ExternalKey, DateOnly? StartDate, bool IsBlocked, string? StatusNote, IReadOnlyList<Guid> DependsOn, IReadOnlyList<NamedLabel> Labels);

    private sealed record NamedLabel(Guid Id, string Name, string Color);

    private sealed record EditedTask(DateOnly? StartDate, bool IsBlocked, string? StatusNote, byte[] RowVersion);

    private sealed record ActivityPage(List<ActivityItem> Items);

    private sealed record ActivityItem(Guid? TaskId, bool ViaApiKey);

    private sealed record SyncResponse(bool Applied, IReadOnlyList<SyncItem> Items);

    private sealed record SyncItem(string ExternalKey, string Outcome, Guid? TaskId, string? ErrorCode, string? Message);
}
