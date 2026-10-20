using System.Net;
using System.Net.Http.Json;
using TaskFlow.Api.IntegrationTests.Infrastructure;

namespace TaskFlow.Api.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class TasksTests(SqlServerApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task NewTasksGoToTheBottomOfToDo()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? string.Empty);
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);

        using var response = await owner.PostAsJsonAsync(
            $"/api/projects/{project.Id}/tasks", new { title = "Wireframes", priority = "High", dueDate = Today.AddDays(3) }, Ct);
        var second = await owner.CreateTaskAsync(project.Id, new { title = "Copy" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var first = await response.Content.ReadFromJsonAsync<TaskResponse>(Ct);
        response.Headers.Location!.AbsolutePath.ShouldBe($"/api/projects/{project.Id}/tasks/{first!.Id}");
        first.Status.ShouldBe("ToDo");
        first.Priority.ShouldBe("High");
        first.IsOverdue.ShouldBeFalse();
        first.Position.ShouldBe(0);
        second.Priority.ShouldBe("Medium");
        second.Position.ShouldBe(1);
    }

    [Theory]
    [InlineData("en", "'Due Date' must be greater than or equal to")]
    [InlineData("es", "'Due Date' debe ser mayor o igual que")]
    public async Task DueDateCannotBeInThePastOnCreate(string language, string expectedMessageStart)
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? string.Empty);
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        owner.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);

        using var response = await owner.PostAsJsonAsync(
            $"/api/projects/{project.Id}/tasks", new { title = "Late", dueDate = Today.AddDays(-1) }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ProblemResponse>(Ct))!.Errors!["dueDate"].ShouldHaveSingleItem()
            .ShouldStartWith(expectedMessageStart);
    }

    [Fact]
    public async Task AssigneeMustBeAProjectMember()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? string.Empty);
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        var (outsiderClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        using var outsider = outsiderClient;
        var project = await factory.CreateProjectAsync(owner);

        using var response = await owner.PostAsJsonAsync(
            $"/api/projects/{project.Id}/tasks", new { title = "Design", assigneeId = await outsider.GetMyIdAsync() }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ProblemResponse>(Ct))!.Code.ShouldBe("Projects.AssigneeNotMember");
    }

    [Fact]
    public async Task MovingRenumbersBothColumnsAndTracksCompletion()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? string.Empty);
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        var a = await owner.CreateTaskAsync(project.Id, new { title = "A" });
        await owner.CreateTaskAsync(project.Id, new { title = "B" });
        var c = await owner.CreateTaskAsync(project.Id, new { title = "C" });

        await MoveAsync(owner, project.Id, c, "InProgress", 0);
        var movedA = await MoveAsync(owner, project.Id, a, "InProgress", 0);
        var tasks = await owner.GetFromJsonAsync<List<TaskResponse>>($"/api/projects/{project.Id}/tasks", Ct);

        tasks!.Select(task => (task.Title, task.Status, task.Position)).ShouldBe(
        [
            ("B", "ToDo", 0),
            ("A", "InProgress", 0),
            ("C", "InProgress", 1),
        ]);

        var done = await MoveAsync(owner, project.Id, movedA, "Done", 99);
        done.Position.ShouldBe(0);
        done.CompletedAt.ShouldNotBeNull();
        (await MoveAsync(owner, project.Id, done, "Review", 0)).CompletedAt.ShouldBeNull();
    }

    [Fact]
    public async Task EditWithAStaleRowVersionConflicts()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? string.Empty);
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        var task = await owner.CreateTaskAsync(project.Id, new { title = "Logo" });
        var uri = $"/api/projects/{project.Id}/tasks/{task.Id}";

        using var first = await owner.PutAsJsonAsync(uri, new { title = "Logo v2", priority = "Low", task.RowVersion }, Ct);
        using var stale = await owner.PutAsJsonAsync(uri, new { title = "Logo v3", priority = "Low", task.RowVersion }, Ct);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await first.Content.ReadFromJsonAsync<TaskResponse>(Ct))!.RowVersion.ShouldNotBe(task.RowVersion);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await stale.Content.ReadFromJsonAsync<ProblemResponse>(Ct))!.Code.ShouldBe("Concurrency.Stale");
    }

    [Fact]
    public async Task FiltersCombine()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? string.Empty);
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        var ownerId = await owner.GetMyIdAsync();
        await owner.CreateTaskAsync(project.Id, new { title = "Mine, urgent", priority = "Critical", assigneeId = ownerId });
        await owner.CreateTaskAsync(project.Id, new { title = "Mine, later", priority = "Low", assigneeId = ownerId });
        await owner.CreateTaskAsync(project.Id, new { title = "Nobody's, urgent", priority = "Critical" });

        var mine = await owner.GetFromJsonAsync<List<TaskResponse>>($"/api/projects/{project.Id}/tasks?assigneeId={ownerId}", Ct);
        var mineAndUrgent = await owner.GetFromJsonAsync<List<TaskResponse>>(
            $"/api/projects/{project.Id}/tasks?assigneeId={ownerId}&priority=Critical", Ct);
        var overdue = await owner.GetFromJsonAsync<List<TaskResponse>>($"/api/projects/{project.Id}/tasks?overdue=true", Ct);

        mine!.Count.ShouldBe(2);
        mineAndUrgent!.ShouldHaveSingleItem().Title.ShouldBe("Mine, urgent");
        overdue.ShouldBeEmpty();
    }

    [Fact]
    public async Task DeletedTaskIsGone()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? string.Empty);
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        var task = await owner.CreateTaskAsync(project.Id, new { title = "Temporary" });
        var uri = new Uri($"/api/projects/{project.Id}/tasks/{task.Id}", UriKind.Relative);

        using var delete = await owner.DeleteAsync(uri, Ct);
        using var get = await owner.GetAsync(uri, Ct);

        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        get.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await get.Content.ReadFromJsonAsync<ProblemResponse>(Ct))!.Code.ShouldBe("Tasks.NotFound");
    }

    private static async Task<TaskResponse> MoveAsync(HttpClient client, Guid projectId, TaskResponse task, string status, int position)
    {
        using var response = await client.PutAsJsonAsync(
            $"/api/projects/{projectId}/tasks/{task.Id}/position", new { status, position, task.RowVersion }, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TaskResponse>(Ct))!;
    }
}
