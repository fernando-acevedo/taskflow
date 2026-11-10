using System.Net;
using System.Net.Http.Json;
using TaskFlow.Api.IntegrationTests.Infrastructure;

namespace TaskFlow.Api.IntegrationTests;

/// <summary>ETag, If-None-Match and If-Match on resources that carry a rowversion (ADR 0022).</summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class ConditionalRequestTests(SqlServerApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record IdResponse(Guid Id);

    private static string Tag(byte[] rowVersion) => $"\"{Convert.ToBase64String(rowVersion)}\"";

    private static HttpRequestMessage Request(HttpMethod method, string url, string header, string value, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation(header, value);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private async Task<(HttpClient Owner, ProjectResponse Project, TaskResponse Task)> SetUpAsync()
    {
        var (owner, _) = await factory.CreateUserClientAsync();
        var project = await factory.CreateProjectAsync(owner);
        var task = await owner.CreateTaskAsync(project.Id, new { title = "Write the brief" });
        return (owner, project, task);
    }

    [Fact]
    public async Task GetsAnswerAStrongETagAndAnswer304WhenItMatches()
    {
        var (owner, project, task) = await SetUpAsync();
        using var _ = owner;
        var url = $"/api/projects/{project.Id}/tasks/{task.Id}";

        using var first = await owner.GetAsync(new Uri(url, UriKind.Relative), Ct);
        using var again = await owner.SendAsync(Request(HttpMethod.Get, url, "If-None-Match", Tag(task.RowVersion)), Ct);
        using var projectResponse = await owner.GetAsync(new Uri($"/api/projects/{project.Id}", UriKind.Relative), Ct);

        first.Headers.ETag!.Tag.ShouldBe(Tag(task.RowVersion));
        first.Headers.ETag.IsWeak.ShouldBeFalse();
        again.StatusCode.ShouldBe(HttpStatusCode.NotModified);
        again.Headers.ETag!.Tag.ShouldBe(Tag(task.RowVersion));
        again.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        projectResponse.Headers.ETag.ShouldNotBeNull();
    }

    [Fact]
    public async Task ATaskListHasAWeakETagThatChangesWithTheData()
    {
        var (owner, project, task) = await SetUpAsync();
        using var _ = owner;
        var url = $"/api/projects/{project.Id}/tasks";

        using var first = await owner.GetAsync(new Uri(url, UriKind.Relative), Ct);
        var tag = first.Headers.ETag!;
        using var same = await owner.SendAsync(Request(HttpMethod.Get, url, "If-None-Match", tag.ToString()), Ct);
        using var filtered = await owner.GetAsync(new Uri($"{url}?priority=High", UriKind.Relative), Ct);
        await owner.MoveTaskAsync(project.Id, task, "InProgress", 0);
        using var changed = await owner.SendAsync(Request(HttpMethod.Get, url, "If-None-Match", tag.ToString()), Ct);

        tag.IsWeak.ShouldBeTrue();
        same.StatusCode.ShouldBe(HttpStatusCode.NotModified);
        filtered.Headers.ETag!.Tag.ShouldNotBe(tag.Tag);
        changed.StatusCode.ShouldBe(HttpStatusCode.OK);
        changed.Headers.ETag!.Tag.ShouldNotBe(tag.Tag);
    }

    [Fact]
    public async Task AnOldIfMatchAnswers412AndTheRightOneWorksWithoutARowVersionInTheBody()
    {
        var (owner, project, task) = await SetUpAsync();
        using var _ = owner;
        var url = $"/api/projects/{project.Id}/tasks/{task.Id}";
        var body = new { title = "Write the brief v2", priority = "Medium" };

        using var good = await owner.SendAsync(Request(HttpMethod.Put, url, "If-Match", Tag(task.RowVersion), body), Ct);
        var updated = await good.ReadAsync<TaskResponse>();
        using var old = await owner.SendAsync(Request(HttpMethod.Put, url, "If-Match", Tag(task.RowVersion), body), Ct);
        using var garbage = await owner.SendAsync(Request(HttpMethod.Put, url, "If-Match", "\"nope\"", body), Ct);
        using var oldDelete = await owner.SendAsync(Request(HttpMethod.Delete, url, "If-Match", Tag(task.RowVersion)), Ct);

        good.StatusCode.ShouldBe(HttpStatusCode.OK);
        updated.RowVersion.ShouldNotBe(task.RowVersion);
        old.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await old.ReadAsync<ProblemResponse>()).Code.ShouldBe("Concurrency.PreconditionFailed");
        garbage.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        oldDelete.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        using var deleted = await owner.SendAsync(Request(HttpMethod.Delete, url, "If-Match", Tag(updated.RowVersion)), Ct);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task MoveAndClientArchiveAcceptIfMatchWithoutABody()
    {
        var (owner, project, task) = await SetUpAsync();
        using var _ = owner;
        using var admin = await factory.CreateAdminClientAsync();
        var client = await admin.CreateClientAsync();

        using var move = await owner.SendAsync(
            Request(HttpMethod.Put, $"/api/projects/{project.Id}/tasks/{task.Id}/position", "If-Match", Tag(task.RowVersion), new { status = "InProgress", position = 0 }), Ct);
        using var staleMove = await owner.SendAsync(
            Request(HttpMethod.Put, $"/api/projects/{project.Id}/tasks/{task.Id}/position", "If-Match", Tag(task.RowVersion), new { status = "Done", position = 0 }), Ct);
        using var archive = await admin.SendAsync(
            Request(HttpMethod.Post, $"/api/clients/{client.Id}/archive", "If-Match", Tag(client.RowVersion)), Ct);
        using var staleRestore = await admin.SendAsync(
            Request(HttpMethod.Post, $"/api/clients/{client.Id}/restore", "If-Match", Tag(client.RowVersion)), Ct);

        move.StatusCode.ShouldBe(HttpStatusCode.OK);
        staleMove.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        archive.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        staleRestore.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
    }

    [Fact]
    public async Task ABodyRowVersionWithoutIfMatchStillAnswers409()
    {
        var (owner, project, task) = await SetUpAsync();
        using var _ = owner;
        var url = $"/api/projects/{project.Id}/tasks/{task.Id}";
        var body = new { title = "Write the brief v2", priority = "Medium", task.RowVersion };

        using var first = await owner.PutAsJsonAsync(url, body, Ct);
        using var second = await owner.PutAsJsonAsync(url, body, Ct);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await second.ReadAsync<ProblemResponse>()).Code.ShouldBe("Concurrency.Stale");
    }

    [Fact]
    public async Task ETagResponsesAreNotSharedAcrossUsersAndSayWhoTheyVaryBy()
    {
        var (owner, project, task) = await SetUpAsync();
        using var _ = owner;
        var (outsider, _) = await factory.CreateUserClientAsync();
        using var __ = outsider;
        var url = $"/api/projects/{project.Id}/tasks";

        using var first = await owner.GetAsync(new Uri(url, UriKind.Relative), Ct);
        using var single = await owner.GetAsync(new Uri($"{url}/{task.Id}", UriKind.Relative), Ct);
        using var other = await outsider.SendAsync(Request(HttpMethod.Get, url, "If-None-Match", first.Headers.ETag!.ToString()), Ct);

        foreach (var response in new[] { first, single })
        {
            response.Headers.ETag.ShouldNotBeNull();
            response.Headers.CacheControl!.Private.ShouldBeTrue();
            response.Headers.CacheControl.NoCache.ShouldBeTrue();
            response.Headers.Vary.ShouldContain("Authorization");
            response.Headers.Vary.ShouldContain("X-Tenant");
        }

        other.StatusCode.ShouldNotBe(HttpStatusCode.NotModified);
        other.IsSuccessStatusCode.ShouldBeFalse();
    }

    [Fact]
    public async Task IfMatchAcceptsAListAndAStarAndRejectsWeakValues()
    {
        var (owner, project, task) = await SetUpAsync();
        using var _ = owner;
        var url = $"/api/projects/{project.Id}/tasks/{task.Id}";
        var body = new { title = "Write the brief v2", priority = "Medium" };
        var listHeader = $"\"AAAAAAAAAAA=\", {Tag(task.RowVersion)}";

        using var weak = await owner.SendAsync(Request(HttpMethod.Put, url, "If-Match", "W/" + Tag(task.RowVersion), body), Ct);
        using var list = await owner.SendAsync(Request(HttpMethod.Put, url, "If-Match", listHeader, body), Ct);
        var updated = await list.ReadAsync<TaskResponse>();
        using var staleList = await owner.SendAsync(Request(HttpMethod.Put, url, "If-Match", listHeader, body), Ct);
        using var star = await owner.SendAsync(Request(HttpMethod.Put, url, "If-Match", "*", new { title = "Write the brief v3", priority = "Low" }), Ct);
        using var starOnMissing = await owner.SendAsync(
            Request(HttpMethod.Put, $"/api/projects/{project.Id}/tasks/{Guid.NewGuid()}", "If-Match", "*", body), Ct);

        weak.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        updated.RowVersion.ShouldNotBe(task.RowVersion);
        staleList.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        star.StatusCode.ShouldBe(HttpStatusCode.OK);
        starOnMissing.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
    }

    [Fact]
    public async Task ClientLabelAndCommentWritesWithoutAVersionAnswer428()
    {
        var (owner, project, task) = await SetUpAsync();
        using var _ = owner;
        using var admin = await factory.CreateAdminClientAsync();
        var client = await admin.CreateClientAsync();
        using var labelResponse = await owner.PostAsJsonAsync($"/api/projects/{project.Id}/labels", new { name = "Bug", color = "#FF0000" }, Ct);
        var label = await labelResponse.ReadAsync<IdResponse>();
        using var commentResponse = await owner.PostAsJsonAsync($"/api/projects/{project.Id}/tasks/{task.Id}/comments", new { body = "Hi" }, Ct);
        var comment = await commentResponse.ReadAsync<IdResponse>();

        using var updateClient = await admin.PutAsJsonAsync($"/api/clients/{client.Id}", new { name = "Renamed" }, Ct);
        using var archive = await admin.PostAsync(new Uri($"/api/clients/{client.Id}/archive", UriKind.Relative), null, Ct);
        using var restore = await admin.PostAsync(new Uri($"/api/clients/{client.Id}/restore", UriKind.Relative), null, Ct);
        using var updateLabel = await owner.PutAsJsonAsync($"/api/projects/{project.Id}/labels/{label.Id}", new { name = "Bug2", color = "#FF0000" }, Ct);
        using var updateComment = await owner.PutAsJsonAsync($"/api/projects/{project.Id}/tasks/{task.Id}/comments/{comment.Id}", new { body = "Edited" }, Ct);
        using var withIfMatch = await admin.SendAsync(
            Request(HttpMethod.Put, $"/api/clients/{client.Id}", "If-Match", Tag(client.RowVersion), new { name = "Renamed" }), Ct);

        foreach (var response in new[] { updateClient, archive, restore, updateLabel, updateComment })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
            (await response.ReadAsync<ProblemResponse>()).Code.ShouldBe("Concurrency.PreconditionRequired");
        }

        withIfMatch.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task CorsExposesTheConditionalHeaders()
    {
        using var custom = factory.WithWebHostBuilder(builder => builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost:4200"));
        using var client = custom.CreateClient();
        const string origin = "http://localhost:4200";

        using var request = Request(HttpMethod.Get, "/api/system/info", "Origin", origin);
        using var response = await client.SendAsync(request, Ct);

        response.Headers.GetValues("Access-Control-Expose-Headers").Single().ShouldContain("ETag");
    }
}
