using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using TaskFlow.Api.IntegrationTests.Infrastructure;
using TaskFlow.Domain.Tenancy;
using TaskFlow.Infrastructure.Tenancy;

namespace TaskFlow.Api.IntegrationTests;

/// <summary>The SignalR hub (ADR 0023): who may join, and the events a change publishes after its commit (ADR 0024).</summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class RealtimeTests(SqlServerApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task JoiningWithoutAccessFails()
    {
        using var owner = (await factory.CreateUserClientAsync()).Client;
        var project = await factory.CreateProjectAsync(owner);
        using var outsider = (await factory.CreateUserClientAsync()).Client;

        await using var listener = await ConnectAsync(outsider);
        var failure = await Should.ThrowAsync<HubException>(() => listener.Connection.InvokeAsync("JoinProject", project.Id, Ct));
        failure.Message.ShouldContain("Projects.");
        await Should.ThrowAsync<HubException>(() => listener.Connection.InvokeAsync("JoinProject", Guid.NewGuid(), Ct));
    }

    [Fact]
    public async Task ConnectingNeedsAValidToken()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "hubs/projects"), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        await using (connection)
        {
            await Should.ThrowAsync<HttpRequestException>(() => connection.StartAsync(Ct));
        }
    }

    [Fact]
    public async Task TaskChangesReachTheOtherTabsWithTheOriginClientId()
    {
        var team = await TeamAsync();
        team.Owner.DefaultRequestHeaders.Add("X-Client-Id", "tab-a");
        await using var watcher = await ConnectAsync(team.Member);
        await watcher.Connection.InvokeAsync("JoinProject", team.Project.Id, Ct);

        // Created: taskChanged with the task as the API serves it.
        var first = await team.Owner.CreateTaskAsync(team.Project.Id, new { title = "First" });
        var second = await team.Owner.CreateTaskAsync(team.Project.Id, new { title = "Second" });
        var created = await watcher.NextAsync("taskChanged", e => TaskId(e) == first.Id);
        created.GetProperty("originClientId").GetString().ShouldBe("tab-a");
        created.GetProperty("projectId").GetGuid().ShouldBe(team.Project.Id);
        created.GetProperty("task").GetProperty("title").GetString().ShouldBe("First");
        created.GetProperty("task").GetProperty("status").GetString().ShouldBe("ToDo");
        await watcher.NextAsync("taskChanged", e => TaskId(e) == second.Id);

        // Moved: the moved task, and the neighbor the renumbering touched.
        await team.Owner.MoveTaskAsync(team.Project.Id, first, "InProgress", 0);
        var moved = await watcher.NextAsync("taskChanged", e => TaskId(e) == first.Id && e.GetProperty("task").GetProperty("status").GetString() == "InProgress");
        moved.GetProperty("originClientId").GetString().ShouldBe("tab-a");
        var reordered = await watcher.NextAsync("tasksReordered");
        reordered.GetProperty("originClientId").GetString().ShouldBe("tab-a");
        var positions = reordered.GetProperty("tasks").EnumerateArray().ToList();
        var neighbor = positions.ShouldHaveSingleItem();
        neighbor.GetProperty("id").GetGuid().ShouldBe(second.Id);
        neighbor.GetProperty("position").GetInt32().ShouldBe(0);
        neighbor.GetProperty("status").GetString().ShouldBe("ToDo");
        neighbor.GetProperty("rowVersion").GetString().ShouldNotBeNullOrEmpty();

        // Commented.
        using var comment = await team.Owner.PostAsJsonAsync(
            $"/api/projects/{team.Project.Id}/tasks/{first.Id}/comments", new { body = "Looks good" }, Ct);
        comment.EnsureSuccessStatusCode();
        var commented = await watcher.NextAsync("commentsChanged");
        commented.GetProperty("taskId").GetGuid().ShouldBe(first.Id);
        commented.GetProperty("originClientId").GetString().ShouldBe("tab-a");

        // Deleted.
        using var deleted = await team.Owner.DeleteAsync(new Uri($"/api/projects/{team.Project.Id}/tasks/{second.Id}", UriKind.Relative), Ct);
        deleted.EnsureSuccessStatusCode();
        var gone = await watcher.NextAsync("taskDeleted");
        gone.GetProperty("taskId").GetGuid().ShouldBe(second.Id);
        gone.GetProperty("projectId").GetGuid().ShouldBe(team.Project.Id);
        gone.GetProperty("originClientId").GetString().ShouldBe("tab-a");
    }

    [Fact]
    public async Task AChangeWithoutAClientIdHasANullOrigin()
    {
        var team = await TeamAsync();
        await using var watcher = await ConnectAsync(team.Member, HttpTransportType.LongPolling);
        await watcher.Connection.InvokeAsync("JoinProject", team.Project.Id, Ct);

        // An id that is not a short token is ignored, like a missing one.
        team.Owner.DefaultRequestHeaders.Add("X-Client-Id", "not a valid id!");
        var task = await team.Owner.CreateTaskAsync(team.Project.Id, new { title = "Anonymous" });

        var changed = await watcher.NextAsync("taskChanged", e => TaskId(e) == task.Id);
        changed.GetProperty("originClientId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task AChangeThatIsRolledBackIsNeverPublished()
    {
        var team = await TeamAsync();
        await using var watcher = await ConnectAsync(team.Member);
        await watcher.Connection.InvokeAsync("JoinProject", team.Project.Id, Ct);

        // A stale row version is rejected before anything is saved.
        var task = await team.Owner.CreateTaskAsync(team.Project.Id, new { title = "Stale" });
        await watcher.NextAsync("taskChanged", e => TaskId(e) == task.Id);
        using var stale = await team.Owner.PutAsJsonAsync(
            $"/api/projects/{team.Project.Id}/tasks/{task.Id}",
            new { title = "Never", priority = "Medium", rowVersion = Convert.ToBase64String([1, 2, 3, 4, 5, 6, 7, 8]) },
            Ct);
        stale.IsSuccessStatusCode.ShouldBeFalse();

        await watcher.ExpectNothingAsync("taskChanged");
    }

    [Fact]
    public async Task AnotherTenantReceivesNothingAndCannotJoin()
    {
        var team = await TeamAsync();

        using var admin = await factory.CreateAdminClientAsync();
        using var createdTenant = await admin.PostAsJsonAsync("/api/tenants", new { name = "Other agency", slug = $"t-{Guid.NewGuid():N}"[..20] }, Ct);
        createdTenant.EnsureSuccessStatusCode();
        var otherTenant = (await createdTenant.ReadAsync<JsonElement>()).GetProperty("tenant").GetProperty("id").GetGuid();
        var (stranger, email) = await factory.CreateUserClientAsync(inDefaultTenant: false);
        using var strangerClient = stranger;
        await factory.AddToTenantAsync(email, otherTenant, TenantRole.Owner);

        await using var spy = await ConnectAsync(strangerClient, tenant: otherTenant);
        await Should.ThrowAsync<HubException>(() => spy.Connection.InvokeAsync("JoinProject", team.Project.Id, Ct));

        // Not even asking for the home tenant helps: they are not a member of it.
        var home = TenantDefaults.DefaultId;
        await Should.ThrowAsync<HttpRequestException>(() => ConnectAsync(strangerClient, tenant: home));

        await team.Owner.CreateTaskAsync(team.Project.Id, new { title = "Private" });
        await spy.ExpectNothingAsync("taskChanged");
    }

    [Fact]
    public async Task ARemovedMemberStopsReceivingTheProjectsEvents()
    {
        var team = await TeamAsync();
        await using var watcher = await ConnectAsync(team.Member);
        await watcher.Connection.InvokeAsync("JoinProject", team.Project.Id, Ct);
        var before = await team.Owner.CreateTaskAsync(team.Project.Id, new { title = "Before" });
        await watcher.NextAsync("taskChanged", e => TaskId(e) == before.Id);

        var memberId = await team.Member.GetMyIdAsync();
        using var removed = await team.Owner.DeleteAsync(new Uri($"/api/projects/{team.Project.Id}/members/{memberId}", UriKind.Relative), Ct);
        removed.EnsureSuccessStatusCode();
        await team.Owner.CreateTaskAsync(team.Project.Id, new { title = "After" });

        await watcher.ExpectNothingAsync("taskChanged");
        watcher.Connection.State.ShouldBe(HubConnectionState.Connected);
    }

    [Fact]
    public async Task ADeactivatedUserIsDisconnected()
    {
        var team = await TeamAsync();
        await using var watcher = await ConnectAsync(team.Member);
        await watcher.Connection.InvokeAsync("JoinProject", team.Project.Id, Ct);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.Connection.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };

        using var admin = await factory.CreateAdminClientAsync();
        using var deactivated = await admin.DeactivateUserAsync(await team.Member.GetMyIdAsync());
        deactivated.EnsureSuccessStatusCode();

        await closed.Task.WaitAsync(Patience, Ct);
        watcher.Connection.State.ShouldBe(HubConnectionState.Disconnected);
    }

    [Fact]
    public async Task ARemovedTenantMemberIsDisconnected()
    {
        var team = await TeamAsync();
        await using var watcher = await ConnectAsync(team.Member);
        await watcher.Connection.InvokeAsync("JoinProject", team.Project.Id, Ct);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.Connection.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };

        using var admin = await factory.CreateAdminClientAsync();
        using var removed = await admin.DeleteAsync(new Uri($"/api/tenant/members/{await team.Member.GetMyIdAsync()}", UriKind.Relative), Ct);
        removed.EnsureSuccessStatusCode();

        await closed.Task.WaitAsync(Patience, Ct);
    }

    private static Guid TaskId(JsonElement payload) => payload.GetProperty("task").GetProperty("id").GetGuid();

    private async Task<Team> TeamAsync()
    {
        var (owner, _) = await factory.CreateUserClientAsync();
        var (member, memberEmail) = await factory.CreateUserClientAsync();
        var project = await factory.CreateProjectAsync(owner);
        using var added = await owner.AddMemberAsync(project.Id, memberEmail);
        added.EnsureSuccessStatusCode();
        return new Team(owner, member, project);
    }

    private async Task<Listener> ConnectAsync(HttpClient client, HttpTransportType transport = HttpTransportType.WebSockets, Guid? tenant = null)
    {
        var token = client.DefaultRequestHeaders.Authorization!.Parameter!;
        var url = new Uri(factory.Server.BaseAddress, tenant is { } id ? $"hubs/projects?tenant={id}" : "hubs/projects");
        var connection = new HubConnectionBuilder()
            .WithUrl(url, options =>
            {
                options.Transports = transport;
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.WebSocketFactory = (context, cancellationToken) => new ValueTask<System.Net.WebSockets.WebSocket>(
                    factory.Server.CreateWebSocketClient().ConnectAsync(new Uri($"{context.Uri}&access_token={Uri.EscapeDataString(token)}"), cancellationToken)); // as a browser does: no headers on a WebSocket
            })
            .Build();
        var listener = new Listener(connection);
        await connection.StartAsync(Ct);
        return listener;
    }

    private sealed record Team(HttpClient Owner, HttpClient Member, ProjectResponse Project);

    private sealed class Listener : IAsyncDisposable
    {
        private readonly Channel<(string Name, JsonElement Payload)> _events = Channel.CreateUnbounded<(string, JsonElement)>();

        public Listener(HubConnection connection)
        {
            Connection = connection;
            foreach (var name in new[] { "taskChanged", "taskDeleted", "tasksReordered", "commentsChanged" })
            {
                connection.On<JsonElement>(name, payload => _events.Writer.TryWrite((name, payload)));
            }
        }

        public HubConnection Connection { get; }

        /// <summary>The next event with that name (and that passes <paramref name="match"/>); earlier events are skipped.</summary>
        public async Task<JsonElement> NextAsync(string name, Func<JsonElement, bool>? match = null)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            timeout.CancelAfter(Patience);
            while (true)
            {
                var next = await _events.Reader.ReadAsync(timeout.Token);
                if (next.Name == name && (match is null || match(next.Payload)))
                {
                    return next.Payload;
                }
            }
        }

        public async Task ExpectNothingAsync(string name)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), Ct);
            while (_events.Reader.TryRead(out var next))
            {
                next.Name.ShouldNotBe(name);
            }
        }

        public async ValueTask DisposeAsync() => await Connection.DisposeAsync();
    }
}
