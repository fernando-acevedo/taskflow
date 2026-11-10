using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TaskFlow.Api.IntegrationTests.Infrastructure;

namespace TaskFlow.Api.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class ActivityTests(SqlServerApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreatingAndMovingATaskEachLeaveAnEntryNewestFirst()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        var ownerId = await owner.GetMyIdAsync();

        var task = await owner.CreateTaskAsync(project.Id, new { title = "Write copy" });
        factory.Time.Advance(TimeSpan.FromMinutes(1));
        await owner.MoveTaskAsync(project.Id, task, "InProgress", 0);
        // Reordering inside the same column is not an event.
        factory.Time.Advance(TimeSpan.FromMinutes(1));

        var page = await owner.GetJsonAsync<PageResponse>(ProjectUrl(project.Id));

        page.TotalCount.ShouldBe(2);
        page.Items.Select(entry => entry.Type).ShouldBe(["TaskMoved", "TaskCreated"]);
        var moved = page.Items[0];
        moved.TaskId.ShouldBe(task.Id);
        moved.ActorId.ShouldBe(ownerId);
        moved.ActorName.ShouldNotBeNullOrWhiteSpace();
        moved.Changes.GetProperty("taskTitle").GetString().ShouldBe("Write copy");
        moved.Changes.GetProperty("from").GetString().ShouldBe("ToDo");
        moved.Changes.GetProperty("to").GetString().ShouldBe("InProgress");
        var created = page.Items[1];
        created.Changes.GetProperty("taskTitle").GetString().ShouldBe("Write copy");
        created.Changes.GetProperty("status").GetString().ShouldBe("ToDo");
    }

    [Fact]
    public async Task EntriesArePagedNewestFirst()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        foreach (var title in new[] { "One", "Two", "Three" })
        {
            await owner.CreateTaskAsync(project.Id, new { title });
            factory.Time.Advance(TimeSpan.FromMinutes(1));
        }

        var first = await owner.GetJsonAsync<PageResponse>(ProjectUrl(project.Id, "?page=1&pageSize=2"));
        var second = await owner.GetJsonAsync<PageResponse>(ProjectUrl(project.Id, "?page=2&pageSize=2"));
        using var invalid = await owner.GetAsync(new Uri(ProjectUrl(project.Id, "?pageSize=0"), UriKind.Relative), Ct);

        first.TotalCount.ShouldBe(3);
        first.Items.Select(entry => entry.Changes.GetProperty("taskTitle").GetString()).ShouldBe(["Three", "Two"]);
        second.Items.Select(entry => entry.Changes.GetProperty("taskTitle").GetString()).ShouldBe(["One"]);
        (second.Page, second.PageSize).ShouldBe((2, 2));
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TheTaskFeedHoldsOnlyThatTasksEntries()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        var first = await owner.CreateTaskAsync(project.Id, new { title = "First" });
        factory.Time.Advance(TimeSpan.FromMinutes(1));
        await owner.CreateTaskAsync(project.Id, new { title = "Second" });
        factory.Time.Advance(TimeSpan.FromMinutes(1));
        using var comment = await owner.PostAsJsonAsync(
            $"/api/projects/{project.Id}/tasks/{first.Id}/comments", new { body = "Hello" }, Ct);
        comment.EnsureSuccessStatusCode();

        var feed = await owner.GetJsonAsync<PageResponse>(TaskUrl(project.Id, first.Id));

        feed.Items.Select(entry => entry.Type).ShouldBe(["CommentAdded", "TaskCreated"]);
        feed.Items.ShouldAllBe(entry => entry.TaskId == first.Id);
    }

    [Fact]
    public async Task MembersAndLabelsAreRecordedWithNames()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        var (memberClient, memberEmail) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        using var member = memberClient;
        var project = await factory.CreateProjectAsync(owner);
        var memberId = await member.GetMyIdAsync();
        (await owner.AddMemberAsync(project.Id, memberEmail)).EnsureSuccessStatusCode();
        factory.Time.Advance(TimeSpan.FromMinutes(1));
        using var label = await owner.PostAsJsonAsync($"/api/projects/{project.Id}/labels", new { name = "Bug", color = "#ff0000" }, Ct);
        label.EnsureSuccessStatusCode();

        var page = await member.GetJsonAsync<PageResponse>(ProjectUrl(project.Id));

        page.Items.Select(entry => entry.Type).ShouldBe(["LabelCreated", "MemberAdded"]);
        page.Items[0].TaskId.ShouldBeNull();
        page.Items[0].Changes.GetProperty("labelName").GetString().ShouldBe("Bug");
        page.Items[1].Changes.GetProperty("memberId").GetGuid().ShouldBe(memberId);
        page.Items[1].Changes.GetProperty("memberName").GetString().ShouldNotBeNullOrWhiteSpace();
        page.Items[1].Changes.GetProperty("role").GetString().ShouldBe("Member");
    }

    [Fact]
    public async Task ANonMemberIsForbiddenAndAnUnknownProjectIsNotFound()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        var (outsiderClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        using var outsider = outsiderClient;
        var project = await factory.CreateProjectAsync(owner);
        var task = await owner.CreateTaskAsync(project.Id, new { title = "Secret" });

        using var projectFeed = await outsider.GetAsync(new Uri(ProjectUrl(project.Id), UriKind.Relative), Ct);
        using var taskFeed = await outsider.GetAsync(new Uri(TaskUrl(project.Id, task.Id), UriKind.Relative), Ct);
        using var unknown = await outsider.GetAsync(new Uri(ProjectUrl(Guid.NewGuid()), UriKind.Relative), Ct);

        projectFeed.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        taskFeed.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task EntriesSurviveDeletingTheTask()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        var task = await owner.CreateTaskAsync(project.Id, new { title = "Temporary" });
        factory.Time.Advance(TimeSpan.FromMinutes(1));

        using var delete = await owner.DeleteAsync(new Uri($"/api/projects/{project.Id}/tasks/{task.Id}", UriKind.Relative), Ct);
        delete.EnsureSuccessStatusCode();

        var projectFeed = await owner.GetJsonAsync<PageResponse>(ProjectUrl(project.Id));
        var taskFeed = await owner.GetJsonAsync<PageResponse>(TaskUrl(project.Id, task.Id));
        projectFeed.Items.Select(entry => entry.Type).ShouldBe(["TaskDeleted", "TaskCreated"]);
        projectFeed.Items.ShouldAllBe(entry => entry.TaskId == task.Id);
        projectFeed.Items[0].Changes.GetProperty("taskTitle").GetString().ShouldBe("Temporary");
        taskFeed.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task AnUpdateWithoutChangesLeavesNoEntry()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        var task = await owner.CreateTaskAsync(project.Id, new { title = "Same", priority = "Medium" });

        using var update = await PutTaskAsync(owner, project.Id, task, new { title = "Same", priority = "Medium" });
        update.EnsureSuccessStatusCode();

        var page = await owner.GetJsonAsync<PageResponse>(ProjectUrl(project.Id));
        page.Items.Select(entry => entry.Type).ShouldBe(["TaskCreated"]);
    }

    [Fact]
    public async Task FailedWritesLeaveNoEntry()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        var (outsiderClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        using var outsider = outsiderClient;
        var outsiderId = await outsider.GetMyIdAsync();
        var project = await factory.CreateProjectAsync(owner);
        var task = await owner.CreateTaskAsync(project.Id, new { title = "Original", priority = "Medium" });
        using var label = await owner.PostAsJsonAsync($"/api/projects/{project.Id}/labels", new { name = "Bug", color = "#ff0000" }, Ct);
        label.EnsureSuccessStatusCode();
        var labelId = (await label.ReadAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var mention = await owner.PostAsJsonAsync(
            $"/api/projects/{project.Id}/tasks/{task.Id}/comments",
            new { body = "Hi", mentionedUserIds = new[] { outsiderId } },
            Ct);
        using var first = await PutTaskAsync(owner, project.Id, task, new { title = "First edit", priority = "Medium" });
        first.EnsureSuccessStatusCode();
        using var stale = await PutTaskAsync(owner, project.Id, task, new { title = "Second edit", priority = "Medium" });
        using var badLabel = await owner.PutAsJsonAsync(
            $"/api/projects/{project.Id}/labels/{labelId}", new { name = new string('x', 500), color = "#ff0000", rowVersion = new byte[8] }, Ct);

        mention.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        badLabel.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var page = await owner.GetJsonAsync<PageResponse>(ProjectUrl(project.Id));
        page.Items.Select(entry => entry.Type).ShouldBe(["TaskUpdated", "LabelCreated", "TaskCreated"]);
    }

    [Fact]
    public async Task ChangingARoleAndRemovingAMemberAreRecorded()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        var (memberClient, memberEmail) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        using var member = memberClient;
        var memberId = await member.GetMyIdAsync();
        var project = await factory.CreateProjectAsync(owner);
        (await owner.AddMemberAsync(project.Id, memberEmail)).EnsureSuccessStatusCode();
        factory.Time.Advance(TimeSpan.FromMinutes(1));
        using var promote = await owner.PutAsJsonAsync($"/api/projects/{project.Id}/members/{memberId}", new { role = "Owner" }, Ct);
        promote.EnsureSuccessStatusCode();
        factory.Time.Advance(TimeSpan.FromMinutes(1));
        using var remove = await owner.DeleteAsync(new Uri($"/api/projects/{project.Id}/members/{memberId}", UriKind.Relative), Ct);
        remove.EnsureSuccessStatusCode();

        var page = await owner.GetJsonAsync<PageResponse>(ProjectUrl(project.Id));

        page.Items.Select(entry => entry.Type).ShouldBe(["MemberRemoved", "MemberRoleChanged", "MemberAdded"]);
        page.Items[0].Changes.GetProperty("memberId").GetGuid().ShouldBe(memberId);
        page.Items[0].Changes.GetProperty("role").GetString().ShouldBe("Owner");
        page.Items[1].Changes.GetProperty("memberId").GetGuid().ShouldBe(memberId);
        page.Items[1].Changes.GetProperty("memberName").GetString().ShouldNotBeNullOrWhiteSpace();
        page.Items[1].Changes.GetProperty("from").GetString().ShouldBe("Member");
        page.Items[1].Changes.GetProperty("to").GetString().ShouldBe("Owner");
    }

    [Fact]
    public async Task RenamingAndDeletingALabelAreRecorded()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        using var created = await owner.PostAsJsonAsync($"/api/projects/{project.Id}/labels", new { name = "Bug", color = "#ff0000" }, Ct);
        created.EnsureSuccessStatusCode();
        var createdLabel = await created.ReadAsync<JsonElement>();
        var labelId = createdLabel.GetProperty("id").GetGuid();
        factory.Time.Advance(TimeSpan.FromMinutes(1));
        using var rename = await owner.PutAsJsonAsync(
            $"/api/projects/{project.Id}/labels/{labelId}", new { name = "Defect", color = "#ff0000", rowVersion = createdLabel.GetProperty("rowVersion").GetString() }, Ct);
        rename.EnsureSuccessStatusCode();
        factory.Time.Advance(TimeSpan.FromMinutes(1));
        using var delete = await owner.DeleteAsync(new Uri($"/api/projects/{project.Id}/labels/{labelId}", UriKind.Relative), Ct);
        delete.EnsureSuccessStatusCode();

        var page = await owner.GetJsonAsync<PageResponse>(ProjectUrl(project.Id));

        page.Items.Select(entry => entry.Type).ShouldBe(["LabelDeleted", "LabelRenamed", "LabelCreated"]);
        page.Items[0].Changes.GetProperty("labelName").GetString().ShouldBe("Defect");
        page.Items[1].Changes.GetProperty("from").GetString().ShouldBe("Bug");
        page.Items[1].Changes.GetProperty("to").GetString().ShouldBe("Defect");
    }

    [Fact]
    public async Task SettingAndClearingTheDueDateKeepBothKeys()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        var task = await owner.CreateTaskAsync(project.Id, new { title = "Dated", priority = "Medium" });
        var due = factory.Today.AddDays(7).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        factory.Time.Advance(TimeSpan.FromMinutes(1));
        using var set = await PutTaskAsync(owner, project.Id, task, new { title = "Dated", priority = "Medium", dueDate = due });
        set.EnsureSuccessStatusCode();
        var withDate = await set.ReadAsync<TaskResponse>();
        factory.Time.Advance(TimeSpan.FromMinutes(1));
        using var clear = await PutTaskAsync(owner, project.Id, withDate, new { title = "Dated", priority = "Medium" });
        clear.EnsureSuccessStatusCode();

        var page = await owner.GetJsonAsync<PageResponse>(ProjectUrl(project.Id));

        page.Items.Select(entry => entry.Type).ShouldBe(["TaskUpdated", "TaskUpdated", "TaskCreated"]);
        var cleared = page.Items[0].Changes.GetProperty("dueDate");
        cleared.GetProperty("from").GetString().ShouldBe(due);
        cleared.GetProperty("to").ValueKind.ShouldBe(JsonValueKind.Null);
        var assigned = page.Items[1].Changes.GetProperty("dueDate");
        assigned.GetProperty("from").ValueKind.ShouldBe(JsonValueKind.Null);
        assigned.GetProperty("to").GetString().ShouldBe(due);
    }

    [Fact]
    public async Task TheTaskFeedOfAnotherProjectsTaskIsEmpty()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        var other = await factory.CreateProjectAsync(owner);
        var foreign = await owner.CreateTaskAsync(other.Id, new { title = "Elsewhere" });

        var feed = await owner.GetJsonAsync<PageResponse>(TaskUrl(project.Id, foreign.Id));

        feed.TotalCount.ShouldBe(0);
        feed.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task EntriesOfOneEditKeepTheOrderTheyWereRecordedIn()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var ownerId = await owner.GetMyIdAsync();
        var project = await factory.CreateProjectAsync(owner);

        // Both entries share one OccurredAt; repeated so that a tie broken by a random id would show up.
        for (var i = 0; i < 8; i++)
        {
            var task = await owner.CreateTaskAsync(project.Id, new { title = $"Task {i}", priority = "Medium" });
            using var update = await PutTaskAsync(owner, project.Id, task, new { title = $"Renamed {i}", priority = "Medium", assigneeId = ownerId });
            update.EnsureSuccessStatusCode();

            var feed = await owner.GetJsonAsync<PageResponse>(TaskUrl(project.Id, task.Id));

            feed.Items.Select(entry => entry.Type).ShouldBe(["TaskAssigned", "TaskUpdated", "TaskCreated"]);
        }
    }

    private static Task<HttpResponseMessage> PutTaskAsync(HttpClient client, Guid projectId, TaskResponse task, object fields)
    {
        var body = JsonSerializer.SerializeToNode(fields)!.AsObject();
        body["rowVersion"] = JsonSerializer.SerializeToNode(task.RowVersion);
        return client.PutAsJsonAsync($"/api/projects/{projectId}/tasks/{task.Id}", body, Ct);
    }

    private static string ProjectUrl(Guid projectId, string query = "") => $"/api/projects/{projectId}/activity{query}";

    private static string TaskUrl(Guid projectId, Guid taskId) => $"/api/projects/{projectId}/tasks/{taskId}/activity";

    private sealed record EntryResponse(
        Guid Id,
        Guid? TaskId,
        Guid ActorId,
        string ActorName,
        string Type,
        DateTimeOffset OccurredAt,
        JsonElement Changes);

    private sealed record PageResponse(List<EntryResponse> Items, int Page, int PageSize, int TotalCount);
}
