using System.Net;
using System.Net.Http.Json;
using TaskFlow.Api.IntegrationTests.Infrastructure;

namespace TaskFlow.Api.IntegrationTests;

/// <summary>Clients, labels and comments reject an edit made on an older version (ADR 0022).</summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class RowVersionTests(SqlServerApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ClientUpdateChecksTheVersionAndChangesIt()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var created = await admin.CreateClientAsync();
        created.RowVersion.ShouldNotBeEmpty();
        var url = $"/api/clients/{created.Id}";

        using var update = await admin.PutAsJsonAsync(url, new { name = $"{created.Name} 2", created.RowVersion }, Ct);
        using var stale = await admin.PutAsJsonAsync(url, new { name = $"{created.Name} 3", created.RowVersion }, Ct);
        using var missing = await admin.PutAsJsonAsync(url, new { name = $"{created.Name} 4" }, Ct);

        update.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var read = await admin.GetJsonAsync<ClientResponse>(url);
        read.RowVersion.ShouldNotBe(created.RowVersion);
        read.Name.ShouldBe($"{created.Name} 2");
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await stale.ReadAsync<ProblemResponse>()).Code.ShouldBe("Concurrency.Stale");
        missing.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
    }

    [Fact]
    public async Task ClientArchiveAndRestoreCheckTheVersion()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var created = await admin.CreateClientAsync();
        var url = $"/api/clients/{created.Id}";

        using var missing = await admin.PostAsJsonAsync($"{url}/archive", new { }, Ct);
        using var archived = await admin.PostAsJsonAsync($"{url}/archive", new { created.RowVersion }, Ct);
        using var staleRestore = await admin.PostAsJsonAsync($"{url}/restore", new { created.RowVersion }, Ct);
        var current = await admin.GetJsonAsync<ClientResponse>(url);
        using var staleArchive = await admin.PostAsJsonAsync($"{url}/archive", new { created.RowVersion }, Ct);
        using var restored = await admin.PostAsJsonAsync($"{url}/restore", new { current.RowVersion }, Ct);

        missing.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
        archived.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        current.IsArchived.ShouldBeTrue();
        current.RowVersion.ShouldNotBe(created.RowVersion);
        staleRestore.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await staleRestore.ReadAsync<ProblemResponse>()).Code.ShouldBe("Concurrency.Stale");
        staleArchive.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        restored.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetJsonAsync<ClientResponse>(url)).IsArchived.ShouldBeFalse();
    }

    [Fact]
    public async Task LabelUpdateChecksTheVersionAndReturnsANewOne()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        using var created = await owner.PostAsJsonAsync($"/api/projects/{project.Id}/labels", new { name = "Bug", color = "#ff0000" }, Ct);
        var label = await created.ReadAsync<LabelBody>();
        label.RowVersion.ShouldNotBeEmpty();
        var url = $"/api/projects/{project.Id}/labels/{label.Id}";

        using var update = await owner.PutAsJsonAsync(url, new { name = "Defect", color = "#00ff00", label.RowVersion }, Ct);
        using var stale = await owner.PutAsJsonAsync(url, new { name = "Other", color = "#00ff00", label.RowVersion }, Ct);
        using var missing = await owner.PutAsJsonAsync(url, new { name = "Other", color = "#00ff00" }, Ct);

        update.StatusCode.ShouldBe(HttpStatusCode.OK);
        var saved = await update.ReadAsync<LabelBody>();
        (saved.Name, saved.RowVersion.SequenceEqual(label.RowVersion)).ShouldBe(("Defect", false));
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await stale.ReadAsync<ProblemResponse>()).Code.ShouldBe("Concurrency.Stale");
        missing.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
    }

    [Fact]
    public async Task CommentUpdateChecksTheVersionAndReturnsANewOne()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        var task = await owner.CreateTaskAsync(project.Id, new { title = "Write copy" });
        var url = $"/api/projects/{project.Id}/tasks/{task.Id}/comments";
        using var created = await owner.PostAsJsonAsync(url, new { body = "Draft" }, Ct);
        var comment = await created.ReadAsync<CommentBody>();
        comment.RowVersion.ShouldNotBeEmpty();

        using var update = await owner.PutAsJsonAsync(
            $"{url}/{comment.Id}", new { body = "Final", mentionedUserIds = Array.Empty<Guid>(), comment.RowVersion }, Ct);
        using var stale = await owner.PutAsJsonAsync(
            $"{url}/{comment.Id}", new { body = "Late", mentionedUserIds = Array.Empty<Guid>(), comment.RowVersion }, Ct);
        using var missing = await owner.PutAsJsonAsync($"{url}/{comment.Id}", new { body = "Late" }, Ct);

        update.StatusCode.ShouldBe(HttpStatusCode.OK);
        var saved = await update.ReadAsync<CommentBody>();
        (saved.Body, saved.RowVersion.SequenceEqual(comment.RowVersion)).ShouldBe(("Final", false));
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await stale.ReadAsync<ProblemResponse>()).Code.ShouldBe("Concurrency.Stale");
        missing.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
        var list = await owner.GetJsonAsync<List<CommentBody>>(url);
        list.ShouldHaveSingleItem().RowVersion.ShouldBe(saved.RowVersion);
    }

    private sealed record LabelBody(Guid Id, string Name, byte[] RowVersion);

    private sealed record CommentBody(Guid Id, string Body, byte[] RowVersion);
}
