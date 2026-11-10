using System.Net;
using System.Net.Http.Json;
using TaskFlow.Api.IntegrationTests.Infrastructure;

namespace TaskFlow.Api.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class TasksTests(SqlServerApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateOnly Today => factory.Today;

    [Fact]
    public async Task NewTasksGoToTheBottomOfToDo()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);

        using var response = await owner.PostAsJsonAsync(
            $"/api/projects/{project.Id}/tasks", new { title = "Wireframes", priority = "High", dueDate = Today.AddDays(3) }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var first = await response.ReadAsync<TaskResponse>();
        var second = await owner.CreateTaskAsync(project.Id, new { title = "Copy" });

        response.Headers.Location!.AbsolutePath.ShouldBe($"/api/projects/{project.Id}/tasks/{first.Id}");
        first.Title.ShouldBe("Wireframes");
        first.Status.ShouldBe("ToDo");
        first.Priority.ShouldBe("High");
        first.DueDate.ShouldBe(Today.AddDays(3));
        first.IsOverdue.ShouldBeFalse();
        first.Position.ShouldBe(0);
        first.CompletedAt.ShouldBeNull();
        second.Priority.ShouldBe("Medium");
        second.Position.ShouldBe(1);
    }

    [Fact]
    public async Task ACreatedTaskKeepsItsStartDateBlockedFlagAndStatusNote()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        var url = $"/api/projects/{project.Id}/tasks";

        using var response = await owner.PostAsJsonAsync(
            url, new { title = "Plan", startDate = Today.AddDays(1), dueDate = Today.AddDays(5), isBlocked = true, statusNote = "  waiting for assets " }, Ct);
        var created = await response.ReadAsync<PlanningResponse>();
        var read = await owner.GetJsonAsync<PlanningResponse>($"{url}/{created.Id}");
        using var afterDue = await owner.PostAsJsonAsync(url, new { title = "Bad", startDate = Today.AddDays(9), dueDate = Today.AddDays(5) }, Ct);
        using var longNote = await owner.PostAsJsonAsync(url, new { title = "Bad", statusNote = new string('x', 501) }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        read.StartDate.ShouldBe(Today.AddDays(1));
        read.IsBlocked.ShouldBeTrue();
        read.StatusNote.ShouldBe("waiting for assets");
        afterDue.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        longNote.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private sealed record PlanningResponse(Guid Id, DateOnly? StartDate, bool IsBlocked, string? StatusNote);

    [Theory]
    [InlineData("en", "'Due Date' must be greater than or equal to")]
    [InlineData("es", "'Due Date' debe ser mayor o igual que")]
    public async Task DueDateCannotBeInThePastOnCreate(string language, string expectedMessageStart)
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        owner.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);

        using var response = await owner.PostAsJsonAsync(
            $"/api/projects/{project.Id}/tasks", new { title = "Late", dueDate = Today.AddDays(-1) }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadAsync<ProblemResponse>()).Errors!["dueDate"].ShouldHaveSingleItem()
            .ShouldStartWith(expectedMessageStart);
    }

    [Fact]
    public async Task AssigneeMustBeAProjectMember()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        var (outsiderClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        using var outsider = outsiderClient;
        var project = await factory.CreateProjectAsync(owner);

        using var response = await owner.PostAsJsonAsync(
            $"/api/projects/{project.Id}/tasks", new { title = "Design", assigneeId = await outsider.GetMyIdAsync() }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadAsync<ProblemResponse>()).Code.ShouldBe("Projects.AssigneeNotMember");
    }

    [Fact]
    public async Task MovingRenumbersBothColumnsAndTracksCompletion()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        var a = await owner.CreateTaskAsync(project.Id, new { title = "A" });
        await owner.CreateTaskAsync(project.Id, new { title = "B" });
        var c = await owner.CreateTaskAsync(project.Id, new { title = "C" });

        await owner.MoveTaskAsync(project.Id, c, "InProgress", 0);
        var movedA = await owner.MoveTaskAsync(project.Id, a, "InProgress", 0);
        var tasks = await owner.GetJsonAsync<List<TaskResponse>>($"/api/projects/{project.Id}/tasks");

        tasks.Select(task => (task.Title, task.Status, task.Position)).ShouldBe(
        [
            ("B", "ToDo", 0),
            ("A", "InProgress", 0),
            ("C", "InProgress", 1),
        ]);

        var done = await owner.MoveTaskAsync(project.Id, movedA, "Done", 99);
        done.Position.ShouldBe(0);
        done.CompletedAt.ShouldBe(factory.Time.GetUtcNow());
        (await owner.MoveTaskAsync(project.Id, done, "Review", 0)).CompletedAt.ShouldBeNull();
    }

    [Fact]
    public async Task MoveReturnsTheReorderedNeighborsWithTheirNewRowVersions()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        var a = await owner.CreateTaskAsync(project.Id, new { title = "A" });
        var b = await owner.CreateTaskAsync(project.Id, new { title = "B" });
        var c = await owner.CreateTaskAsync(project.Id, new { title = "C" });

        var result = await owner.MoveTaskWithResultAsync(project.Id, a, "InProgress", 0);

        result.Task.Id.ShouldBe(a.Id);
        result.Task.Status.ShouldBe("InProgress");
        result.Reordered.Select(r => (r.Id, r.Status, r.Position)).ShouldBe([(b.Id, "ToDo", 0), (c.Id, "ToDo", 1)]);
        var tasks = await owner.GetJsonAsync<List<TaskResponse>>($"/api/projects/{project.Id}/tasks");
        foreach (var reordered in result.Reordered)
        {
            tasks.Single(t => t.Id == reordered.Id).RowVersion.ShouldBe(reordered.RowVersion);
        }
    }

    [Fact]
    public async Task ConcurrentMovesIntoOneColumnLeaveContiguousPositions()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        var created = new List<TaskResponse>();
        for (var i = 0; i < 6; i++)
        {
            created.Add(await owner.CreateTaskAsync(project.Id, new { title = $"T{i}" }));
        }

        var statuses = await Task.WhenAll(created.Select(async task =>
        {
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = owner.DefaultRequestHeaders.Authorization;
            foreach (var header in owner.DefaultRequestHeaders.Where(h => h.Key != "Authorization"))
            {
                client.DefaultRequestHeaders.Add(header.Key, header.Value);
            }

            using var response = await client.TryMoveTaskAsync(project.Id, task, "InProgress", 0);
            return response.StatusCode;
        }));

        statuses.ShouldAllBe(code => code == HttpStatusCode.OK || code == HttpStatusCode.Conflict);
        statuses.ShouldContain(HttpStatusCode.OK);
        var tasks = await owner.GetJsonAsync<List<TaskResponse>>($"/api/projects/{project.Id}/tasks");
        foreach (var column in tasks.GroupBy(t => t.Status))
        {
            column.Select(t => t.Position).Order().ShouldBe(Enumerable.Range(0, column.Count()));
        }
    }

    [Fact]
    public async Task MoveWithAStaleRowVersionConflicts()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        var task = await owner.CreateTaskAsync(project.Id, new { title = "Logo" });

        await owner.MoveTaskAsync(project.Id, task, "InProgress", 0);
        using var stale = await owner.TryMoveTaskAsync(project.Id, task, "Review", 0);

        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await stale.ReadAsync<ProblemResponse>()).Code.ShouldBe("Concurrency.Stale");
        (await owner.GetJsonAsync<TaskResponse>($"/api/projects/{project.Id}/tasks/{task.Id}")).Status.ShouldBe("InProgress");
    }

    [Fact]
    public async Task EditWithAStaleRowVersionConflicts()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        var task = await owner.CreateTaskAsync(project.Id, new { title = "Logo" });
        var uri = $"/api/projects/{project.Id}/tasks/{task.Id}";

        using var first = await owner.PutAsJsonAsync(uri, new { title = "Logo v2", priority = "Low", task.RowVersion }, Ct);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        var edited = await first.ReadAsync<TaskResponse>();
        (edited.Title, edited.Priority).ShouldBe(("Logo v2", "Low"));
        edited.RowVersion.ShouldNotBe(task.RowVersion);

        using var stale = await owner.PutAsJsonAsync(uri, new { title = "Logo v3", priority = "Low", task.RowVersion }, Ct);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await stale.ReadAsync<ProblemResponse>()).Code.ShouldBe("Concurrency.Stale");
        (await owner.GetJsonAsync<TaskResponse>(uri)).Title.ShouldBe("Logo v2");
    }

    [Fact]
    public async Task EditWithAnEmptyTitleIsAFieldError()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        var task = await owner.CreateTaskAsync(project.Id, new { title = "Logo" });

        using var response = await owner.PutAsJsonAsync(
            $"/api/projects/{project.Id}/tasks/{task.Id}", new { title = "", priority = "Low", task.RowVersion }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadAsync<ProblemResponse>()).Errors!.Keys.ShouldBe(["title"]);
    }

    [Fact]
    public async Task FiltersCombine()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        var ownerId = await owner.GetMyIdAsync();
        await owner.CreateTaskAsync(project.Id, new { title = "Mine, urgent", priority = "Critical", assigneeId = ownerId });
        await owner.CreateTaskAsync(project.Id, new { title = "Mine, later", priority = "Low", assigneeId = ownerId });
        await owner.CreateTaskAsync(project.Id, new { title = "Nobody's, urgent", priority = "Critical" });
        var late = await owner.CreateTaskAsync(project.Id, new { title = "Mine, late", assigneeId = ownerId, dueDate = Today });
        await factory.SetTaskDueDateAsync(late.Id, Today.AddDays(-1));

        var mine = await owner.GetJsonAsync<List<TaskResponse>>($"/api/projects/{project.Id}/tasks?assigneeId={ownerId}");
        var mineAndUrgent = await owner.GetJsonAsync<List<TaskResponse>>(
            $"/api/projects/{project.Id}/tasks?assigneeId={ownerId}&priority=Critical");
        var overdue = await owner.GetJsonAsync<List<TaskResponse>>($"/api/projects/{project.Id}/tasks?overdue=true");

        mine.Select(task => task.Title).ShouldBe(["Mine, urgent", "Mine, later", "Mine, late"], ignoreOrder: true);
        mineAndUrgent.ShouldHaveSingleItem().Title.ShouldBe("Mine, urgent");
        var overdueTask = overdue.ShouldHaveSingleItem();
        overdueTask.Title.ShouldBe("Mine, late");
        overdueTask.IsOverdue.ShouldBeTrue();
    }

    [Fact]
    public async Task DeletedTaskIsGone()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        var task = await owner.CreateTaskAsync(project.Id, new { title = "Temporary" });
        var uri = new Uri($"/api/projects/{project.Id}/tasks/{task.Id}", UriKind.Relative);

        using var delete = await owner.DeleteAsync(uri, Ct);
        using var get = await owner.GetAsync(uri, Ct);

        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        get.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await get.ReadAsync<ProblemResponse>()).Code.ShouldBe("Tasks.NotFound");
    }

    [Fact]
    public async Task OutsiderCannotChangeTasks()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        var (outsiderClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        using var outsider = outsiderClient;
        var project = await factory.CreateProjectAsync(owner);
        var task = await owner.CreateTaskAsync(project.Id, new { title = "Logo" });
        var uri = $"/api/projects/{project.Id}/tasks/{task.Id}";

        using var update = await outsider.PutAsJsonAsync(uri, new { title = "Hijacked", priority = "Low", task.RowVersion }, Ct);
        using var move = await outsider.TryMoveTaskAsync(project.Id, task, "Done", 0);
        using var delete = await outsider.DeleteAsync(new Uri(uri, UriKind.Relative), Ct);

        update.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        move.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        delete.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await owner.GetJsonAsync<TaskResponse>(uri)).ShouldSatisfyAllConditions(
            unchanged => unchanged.Title.ShouldBe("Logo"),
            unchanged => unchanged.Status.ShouldBe("ToDo"));
    }

    [Fact]
    public async Task TaskIdFromAnotherProjectIsNotFound()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        var (otherClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        using var other = otherClient;
        var mine = await factory.CreateProjectAsync(owner);
        var theirs = await factory.CreateProjectAsync(other);
        var secret = await other.CreateTaskAsync(theirs.Id, new { title = "Secret" });
        var uri = $"/api/projects/{mine.Id}/tasks/{secret.Id}";

        using var get = await owner.GetAsync(new Uri(uri, UriKind.Relative), Ct);
        using var update = await owner.PutAsJsonAsync(uri, new { title = "Hijacked", priority = "Low", secret.RowVersion }, Ct);
        using var move = await owner.TryMoveTaskAsync(mine.Id, secret, "Done", 0);
        using var delete = await owner.DeleteAsync(new Uri(uri, UriKind.Relative), Ct);

        get.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        update.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        move.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        delete.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.GetJsonAsync<TaskResponse>($"/api/projects/{theirs.Id}/tasks/{secret.Id}")).ShouldSatisfyAllConditions(
            untouched => untouched.Title.ShouldBe("Secret"),
            untouched => untouched.Status.ShouldBe("ToDo"));
    }
}
