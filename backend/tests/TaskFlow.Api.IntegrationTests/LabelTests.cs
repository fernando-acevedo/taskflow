using System.Net;
using System.Net.Http.Json;
using TaskFlow.Api.IntegrationTests.Infrastructure;

namespace TaskFlow.Api.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class LabelTests(SqlServerApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OwnerManagesLabelsAndMembersReadThem()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        var (memberClient, memberEmail) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        using var member = memberClient;
        var project = await factory.CreateProjectAsync(owner);
        (await owner.AddMemberAsync(project.Id, memberEmail)).EnsureSuccessStatusCode();

        var bug = await CreateLabelAsync(owner, project.Id, "Bug", "#ff0000");
        bug.Color.ShouldBe("#FF0000");

        using var forbidden = await member.PostAsJsonAsync($"/api/projects/{project.Id}/labels", new { name = "X", color = "#000000" }, Ct);
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var list = await member.GetJsonAsync<List<LabelResponse>>($"/api/projects/{project.Id}/labels");
        list.ShouldHaveSingleItem().Id.ShouldBe(bug.Id);

        using var renamed = await owner.PutAsJsonAsync($"/api/projects/{project.Id}/labels/{bug.Id}", new { name = "Defect", color = "#00ff00" }, Ct);
        (await renamed.ReadAsync<LabelResponse>()).Name.ShouldBe("Defect");
    }

    [Fact]
    public async Task NamesAreUniqueIgnoringCaseAndColorIsValidated()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        await CreateLabelAsync(owner, project.Id, "Bug", "#FF0000");

        using var duplicate = await owner.PostAsJsonAsync($"/api/projects/{project.Id}/labels", new { name = "bug", color = "#00FF00" }, Ct);
        using var badColor = await owner.PostAsJsonAsync($"/api/projects/{project.Id}/labels", new { name = "Other", color = "red" }, Ct);

        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await duplicate.ReadAsync<ProblemResponse>()).Code.ShouldBe("Labels.NameTaken");
        badColor.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TasksCarryLabelsAndCanBeFilteredByThem()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        var bug = await CreateLabelAsync(owner, project.Id, "Bug", "#FF0000");
        var chore = await CreateLabelAsync(owner, project.Id, "Chore", "#00FF00");

        var tagged = await CreateTaskAsync(owner, project.Id, "Fix it", [bug.Id, chore.Id]);
        await CreateTaskAsync(owner, project.Id, "Plain", null);

        tagged.Labels.Select(label => label.Name).ShouldBe(["Bug", "Chore"]);
        var filtered = await owner.GetJsonAsync<List<LabeledTask>>($"/api/projects/{project.Id}/tasks?labelId={bug.Id}");
        filtered.ShouldHaveSingleItem().Id.ShouldBe(tagged.Id);

        using var update = await owner.PutAsJsonAsync(
            $"/api/projects/{project.Id}/tasks/{tagged.Id}",
            new { title = "Fix it", priority = "Medium", labelIds = new[] { chore.Id }, rowVersion = tagged.RowVersion },
            Ct);
        (await update.ReadAsync<LabeledTask>()).Labels.ShouldHaveSingleItem().Id.ShouldBe(chore.Id);
    }

    [Fact]
    public async Task LabelsOfAnotherProjectAreRejected()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        var other = await factory.CreateProjectAsync(owner);
        var foreign = await CreateLabelAsync(owner, other.Id, "Bug", "#FF0000");

        using var response = await owner.PostAsJsonAsync(
            $"/api/projects/{project.Id}/tasks", new { title = "Task", labelIds = new[] { foreign.Id } }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadAsync<ProblemResponse>()).Code.ShouldBe("Labels.NotInProject");
    }

    [Fact]
    public async Task DeletingALabelRemovesItFromTasks()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        var bug = await CreateLabelAsync(owner, project.Id, "Bug", "#FF0000");
        var task = await CreateTaskAsync(owner, project.Id, "Fix it", [bug.Id]);

        using var delete = await owner.DeleteAsync(new Uri($"/api/projects/{project.Id}/labels/{bug.Id}", UriKind.Relative), Ct);

        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var reloaded = await owner.GetJsonAsync<LabeledTask>($"/api/projects/{project.Id}/tasks/{task.Id}");
        reloaded.Labels.ShouldBeEmpty();
    }

    [Fact]
    public async Task ArchivedProjectLabelsAreReadOnly()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        var bug = await CreateLabelAsync(owner, project.Id, "Bug", "#FF0000");
        (await owner.ArchiveProjectAsync(project.Id)).EnsureSuccessStatusCode();

        using var create = await owner.PostAsJsonAsync($"/api/projects/{project.Id}/labels", new { name = "New", color = "#000000" }, Ct);
        using var delete = await owner.DeleteAsync(new Uri($"/api/projects/{project.Id}/labels/{bug.Id}", UriKind.Relative), Ct);

        create.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        delete.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    private static async Task<LabelResponse> CreateLabelAsync(HttpClient client, Guid projectId, string name, string color)
    {
        using var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/labels", new { name, color }, Ct);
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<LabelResponse>();
    }

    private static async Task<LabeledTask> CreateTaskAsync(HttpClient client, Guid projectId, string title, Guid[]? labelIds)
    {
        using var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/tasks", new { title, labelIds }, Ct);
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<LabeledTask>();
    }

    private sealed record LabelResponse(Guid Id, string Name, string Color);

    private sealed record LabeledTask(Guid Id, byte[] RowVersion, IReadOnlyList<LabelResponse> Labels);
}
