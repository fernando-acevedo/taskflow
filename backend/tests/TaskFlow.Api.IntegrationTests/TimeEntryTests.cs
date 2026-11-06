using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TaskFlow.Api.IntegrationTests.Infrastructure;

namespace TaskFlow.Api.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class TimeEntryTests(SqlServerApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateOnly Today => factory.Today;

    [Fact]
    public async Task MembersLogTheirTimeAndSeeOnlyTheirOwnWithTheFullTotalAndEstimate()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        var (memberClient, memberEmail) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        using var member = memberClient;
        var project = await factory.CreateProjectAsync(owner);
        (await owner.AddMemberAsync(project.Id, memberEmail)).EnsureSuccessStatusCode();
        var memberId = await member.GetMyIdAsync();
        var task = await owner.CreateTaskAsync(project.Id, new { title = "Write copy", estimatedMinutes = 120 });

        var mine = await PostAsync(member, project.Id, task.Id, Today, 45, "  First pass ");
        await PostAsync(owner, project.Id, task.Id, Today.AddDays(-1), 30, null);

        mine.UserId.ShouldBe(memberId);
        mine.UserName.ShouldNotBeNullOrWhiteSpace();
        (mine.Date, mine.Minutes, mine.Note).ShouldBe((Today, 45, "First pass"));
        var time = await member.GetJsonAsync<TaskTimeResponse>(EntriesUrl(project.Id, task.Id));
        time.TotalMinutes.ShouldBe(75);
        time.EstimatedMinutes.ShouldBe(120);
        time.Entries.Select(entry => entry.Minutes).ShouldBe([45]);
        time.Entries.ShouldAllBe(entry => entry.UserName != string.Empty);
        var all = await owner.GetJsonAsync<TaskTimeResponse>(EntriesUrl(project.Id, task.Id));
        all.Entries.Select(entry => entry.Minutes).ShouldBe([45, 30]);
        all.TotalMinutes.ShouldBe(75);
    }

    [Fact]
    public async Task AGlobalAdminWhoIsNotAMemberSeesNoEntriesButTheTotal()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        using var admin = await factory.CreateAdminClientAsync();
        var project = await factory.CreateProjectAsync(owner);
        var task = await owner.CreateTaskAsync(project.Id, new { title = "Write copy", estimatedMinutes = 60 });
        await PostAsync(owner, project.Id, task.Id, Today, 20, null);

        var time = await admin.GetJsonAsync<TaskTimeResponse>(EntriesUrl(project.Id, task.Id));

        time.Entries.ShouldBeEmpty();
        time.TotalMinutes.ShouldBe(20);
        time.EstimatedMinutes.ShouldBe(60);
    }

    [Fact]
    public async Task OnlyOwnersReadTheSummaryAndTheCsv()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        var (memberClient, memberEmail) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        using var member = memberClient;
        var project = await factory.CreateProjectAsync(owner);
        (await owner.AddMemberAsync(project.Id, memberEmail)).EnsureSuccessStatusCode();
        var range = $"from={Today.AddDays(-1):yyyy-MM-dd}&to={Today:yyyy-MM-dd}";

        using var summary = await member.GetAsync(new Uri($"/api/projects/{project.Id}/time-summary", UriKind.Relative), Ct);
        using var csv = await member.GetAsync(new Uri($"/api/projects/{project.Id}/time-summary.csv?{range}", UriKind.Relative), Ct);

        summary.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        csv.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await summary.ReadAsync<ProblemResponse>()).Code.ShouldBe("Projects.Forbidden");
    }

    [Fact]
    public async Task TheDateMarginIsOneDayAheadOfUtc()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        var task = await owner.CreateTaskAsync(project.Id, new { title = "Write copy" });

        using var tomorrow = await owner.PostAsJsonAsync(EntriesUrl(project.Id, task.Id), new { date = Today.AddDays(1), minutes = 30 }, Ct);
        using var inTwoDays = await owner.PostAsJsonAsync(EntriesUrl(project.Id, task.Id), new { date = Today.AddDays(2), minutes = 30 }, Ct);

        tomorrow.StatusCode.ShouldBe(HttpStatusCode.Created);
        inTwoDays.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await inTwoDays.ReadAsync<ProblemResponse>()).Code.ShouldBe("TimeEntries.FutureDate");
    }

    [Fact]
    public async Task FutureDatesAndOutOfRangeMinutesAreRejected()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        var task = await owner.CreateTaskAsync(project.Id, new { title = "Write copy" });

        using var future = await owner.PostAsJsonAsync(EntriesUrl(project.Id, task.Id), new { date = Today.AddDays(2), minutes = 30 }, Ct);
        using var zero = await owner.PostAsJsonAsync(EntriesUrl(project.Id, task.Id), new { date = Today, minutes = 0 }, Ct);
        using var tooMuch = await owner.PostAsJsonAsync(EntriesUrl(project.Id, task.Id), new { date = Today, minutes = 1441 }, Ct);
        using var today = await owner.PostAsJsonAsync(EntriesUrl(project.Id, task.Id), new { date = Today, minutes = 1440 }, Ct);

        future.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await future.ReadAsync<ProblemResponse>()).Code.ShouldBe("TimeEntries.FutureDate");
        zero.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        tooMuch.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        today.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task AnotherMemberCannotEditOrDeleteAnEntry()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        var (memberClient, memberEmail) = await factory.CreateUserClientAsync();
        var (otherClient, otherEmail) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        using var member = memberClient;
        using var other = otherClient;
        var project = await factory.CreateProjectAsync(owner);
        (await owner.AddMemberAsync(project.Id, memberEmail)).EnsureSuccessStatusCode();
        (await owner.AddMemberAsync(project.Id, otherEmail)).EnsureSuccessStatusCode();
        var task = await owner.CreateTaskAsync(project.Id, new { title = "Write copy" });
        var entry = await PostAsync(member, project.Id, task.Id, Today, 30, null);

        using var edit = await other.PutAsJsonAsync(EntryUrl(project.Id, task.Id, entry.Id), new { date = Today, minutes = 60 }, Ct);
        using var delete = await other.DeleteAsync(new Uri(EntryUrl(project.Id, task.Id, entry.Id), UriKind.Relative), Ct);
        using var byAuthor = await member.PutAsJsonAsync(EntryUrl(project.Id, task.Id, entry.Id), new { date = Today, minutes = 50, note = "Fixed" }, Ct);

        edit.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await edit.ReadAsync<ProblemResponse>()).Code.ShouldBe("TimeEntries.NotOwner");
        delete.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        byAuthor.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await byAuthor.ReadAsync<EntryResponse>()).Minutes.ShouldBe(50);
    }

    [Fact]
    public async Task AProjectOwnerEditsAndDeletesEveryonesEntries()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        var (memberClient, memberEmail) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        using var member = memberClient;
        var project = await factory.CreateProjectAsync(owner);
        (await owner.AddMemberAsync(project.Id, memberEmail)).EnsureSuccessStatusCode();
        var memberId = await member.GetMyIdAsync();
        var task = await owner.CreateTaskAsync(project.Id, new { title = "Write copy" });
        var entry = await PostAsync(member, project.Id, task.Id, Today, 30, null);
        var second = await PostAsync(member, project.Id, task.Id, Today, 10, null);

        using var edit = await owner.PutAsJsonAsync(EntryUrl(project.Id, task.Id, entry.Id), new { date = Today.AddDays(-1), minutes = 90, note = "Corrected" }, Ct);
        using var delete = await owner.DeleteAsync(new Uri(EntryUrl(project.Id, task.Id, second.Id), UriKind.Relative), Ct);

        edit.StatusCode.ShouldBe(HttpStatusCode.OK);
        var edited = await edit.ReadAsync<EntryResponse>();
        (edited.UserId, edited.Date, edited.Minutes, edited.Note).ShouldBe((memberId, Today.AddDays(-1), 90, "Corrected"));
        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var time = await owner.GetJsonAsync<TaskTimeResponse>(EntriesUrl(project.Id, task.Id));
        time.Entries.ShouldHaveSingleItem().Id.ShouldBe(entry.Id);
        time.TotalMinutes.ShouldBe(90);
    }

    [Fact]
    public async Task AnArchivedProjectIsReadOnlyButStillListsTime()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        var task = await owner.CreateTaskAsync(project.Id, new { title = "Write copy" });
        var entry = await PostAsync(owner, project.Id, task.Id, Today, 30, null);
        (await owner.ArchiveProjectAsync(project.Id)).EnsureSuccessStatusCode();

        using var create = await owner.PostAsJsonAsync(EntriesUrl(project.Id, task.Id), new { date = Today, minutes = 10 }, Ct);
        using var update = await owner.PutAsJsonAsync(EntryUrl(project.Id, task.Id, entry.Id), new { date = Today, minutes = 20 }, Ct);
        using var delete = await owner.DeleteAsync(new Uri(EntryUrl(project.Id, task.Id, entry.Id), UriKind.Relative), Ct);

        create.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await create.ReadAsync<ProblemResponse>()).Code.ShouldBe("Projects.Archived");
        update.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        delete.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await owner.GetJsonAsync<TaskTimeResponse>(EntriesUrl(project.Id, task.Id))).TotalMinutes.ShouldBe(30);
    }

    [Fact]
    public async Task OutsidersCannotLogOrReadTime()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        var (outsiderClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        using var outsider = outsiderClient;
        var project = await factory.CreateProjectAsync(owner);
        var task = await owner.CreateTaskAsync(project.Id, new { title = "Write copy" });

        using var create = await outsider.PostAsJsonAsync(EntriesUrl(project.Id, task.Id), new { date = Today, minutes = 10 }, Ct);
        using var list = await outsider.GetAsync(new Uri(EntriesUrl(project.Id, task.Id), UriKind.Relative), Ct);
        using var summary = await outsider.GetAsync(new Uri($"/api/projects/{project.Id}/time-summary", UriKind.Relative), Ct);

        create.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        list.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        summary.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TheSummaryTotalsByPersonAndTaskWithinTheFilters()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        var (memberClient, memberEmail) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        using var member = memberClient;
        var project = await factory.CreateProjectAsync(owner);
        (await owner.AddMemberAsync(project.Id, memberEmail)).EnsureSuccessStatusCode();
        var ownerId = await owner.GetMyIdAsync();
        var memberId = await member.GetMyIdAsync();
        var design = await owner.CreateTaskAsync(project.Id, new { title = "Design", estimatedMinutes = 120 });
        var copy = await owner.CreateTaskAsync(project.Id, new { title = "Copy" });
        await PostAsync(owner, project.Id, design.Id, Today, 60, null);
        await PostAsync(member, project.Id, design.Id, Today.AddDays(-1), 30, null);
        await PostAsync(member, project.Id, copy.Id, Today.AddDays(-1), 15, null);
        await PostAsync(owner, project.Id, copy.Id, Today.AddDays(-10), 200, null);

        var all = await owner.GetJsonAsync<SummaryResponse>($"/api/projects/{project.Id}/time-summary");
        var recent = await owner.GetJsonAsync<SummaryResponse>(
            $"/api/projects/{project.Id}/time-summary?from={Today.AddDays(-2):yyyy-MM-dd}&to={Today:yyyy-MM-dd}");
        var onlyMember = await owner.GetJsonAsync<SummaryResponse>(
            $"/api/projects/{project.Id}/time-summary?from={Today.AddDays(-2):yyyy-MM-dd}&userId={memberId}");

        all.TotalMinutes.ShouldBe(305);
        recent.TotalMinutes.ShouldBe(105);
        recent.ByUser.Select(row => (row.UserId, row.Minutes)).ShouldBe([(ownerId, 60), (memberId, 45)]);
        recent.ByTask.Select(row => (row.Title, row.EstimatedMinutes, row.ActualMinutes)).ShouldBe([("Design", (int?)120, 90), ("Copy", null, 15)]);
        onlyMember.TotalMinutes.ShouldBe(45);
        onlyMember.ByUser.ShouldHaveSingleItem().UserId.ShouldBe(memberId);
    }

    [Fact]
    public async Task AnInvalidRangeIsABadRequest()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        var url = $"/api/projects/{project.Id}/time-summary";
        var to = Today;

        using var backwards = await owner.GetAsync(new Uri($"{url}?from={to.AddDays(1):yyyy-MM-dd}&to={to:yyyy-MM-dd}", UriKind.Relative), Ct);
        using var tooLong = await owner.GetAsync(new Uri($"{url}?from={to.AddDays(-366):yyyy-MM-dd}&to={to:yyyy-MM-dd}", UriKind.Relative), Ct);
        using var tooLongCsv = await owner.GetAsync(new Uri($"{url}.csv?from={to.AddDays(-366):yyyy-MM-dd}&to={to:yyyy-MM-dd}", UriKind.Relative), Ct);
        using var csvWithoutTo = await owner.GetAsync(new Uri($"{url}.csv?from={to:yyyy-MM-dd}", UriKind.Relative), Ct);
        using var oneYear = await owner.GetAsync(new Uri($"{url}?from={to.AddDays(-365):yyyy-MM-dd}&to={to:yyyy-MM-dd}", UriKind.Relative), Ct);

        backwards.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await backwards.ReadAsync<ProblemResponse>()).Code.ShouldBe("TimeEntries.InvalidRange");
        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        tooLongCsv.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        csvWithoutTo.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await csvWithoutTo.ReadAsync<ProblemResponse>()).Code.ShouldBe("TimeEntries.InvalidRange");
        oneYear.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TheCsvHasABomAHeaderAndOneRowPerEntry()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        var task = await owner.CreateTaskAsync(project.Id, new { title = "Write, copy" });
        await PostAsync(owner, project.Id, task.Id, Today, 90, "=SUM(1)");
        await PostAsync(owner, project.Id, task.Id, Today.AddDays(-1), 5, "  =HYPERLINK(1)");

        using var response = await owner.GetAsync(new Uri($"/api/projects/{project.Id}/time-summary.csv?from={Today.AddDays(-1):yyyy-MM-dd}&to={Today:yyyy-MM-dd}", UriKind.Relative), Ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        bytes.Take(3).ShouldBe([(byte)0xEF, 0xBB, 0xBF]);
        var lines = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines.Length.ShouldBe(3);
        lines[0].ShouldBe("Date,User,Task,Minutes,Note");
        lines[1].ShouldStartWith($"{Today.AddDays(-1):yyyy-MM-dd},");
        lines[1].ShouldEndWith(",\"Write, copy\",5,'=HYPERLINK(1)");
        lines[2].ShouldStartWith($"{Today:yyyy-MM-dd},");
        lines[2].ShouldEndWith(",\"Write, copy\",90,'=SUM(1)");
    }

    [Fact]
    public async Task TheDashboardWeekFollowsTheClientsLocalDateWithinOneDayOfUtc()
    {
        // The fake clock only moves forward: jump to Monday 01:00 UTC, when a client one zone behind is still on Sunday.
        var sunday = Today.AddDays((7 - (int)Today.DayOfWeek) % 7);
        factory.Time.SetUtcNow(new DateTimeOffset(sunday.AddDays(1).ToDateTime(new TimeOnly(1, 0)), TimeSpan.Zero));
        var (userClient, _) = await factory.CreateUserClientAsync();
        using var user = userClient;
        var project = await factory.CreateProjectAsync(user);
        var task = await user.CreateTaskAsync(project.Id, new { title = "Write copy" });
        await PostAsync(user, project.Id, task.Id, sunday, 40, null);

        var local = await user.GetJsonAsync<WeekDashboardResponse>($"/api/dashboard?today={sunday:yyyy-MM-dd}");
        var utc = await user.GetJsonAsync<WeekDashboardResponse>("/api/dashboard");
        var far = await user.GetJsonAsync<WeekDashboardResponse>($"/api/dashboard?today={sunday.AddDays(-3):yyyy-MM-dd}");

        local.WeekMinutes.ShouldBe(40);
        local.WeekMinutesByDay.ShouldBe([0, 0, 0, 0, 0, 0, 40]);
        utc.WeekMinutes.ShouldBe(0);
        far.WeekMinutes.ShouldBe(0);
    }

    [Fact]
    public async Task OnlyAnEstimateChangeIsRecordedInTheActivityLog()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        var task = await owner.CreateTaskAsync(project.Id, new { title = "Write copy" });

        await PostAsync(owner, project.Id, task.Id, Today, 30, null);
        using var update = await owner.PutAsJsonAsync(
            $"/api/projects/{project.Id}/tasks/{task.Id}",
            new { title = "Write copy", priority = "Medium", estimatedMinutes = 90, rowVersion = task.RowVersion },
            Ct);

        update.StatusCode.ShouldBe(HttpStatusCode.OK);
        var feed = await owner.GetJsonAsync<JsonElement>($"/api/projects/{project.Id}/activity");
        var items = feed.GetProperty("items").EnumerateArray().ToList();
        items.Select(item => item.GetProperty("type").GetString()).ShouldBe(["TaskUpdated", "TaskCreated"]);
        var change = items[0].GetProperty("changes").GetProperty("estimatedMinutes");
        change.GetProperty("from").ValueKind.ShouldBe(JsonValueKind.Null);
        change.GetProperty("to").GetInt32().ShouldBe(90);
    }

    [Fact]
    public async Task TheDashboardCountsTheMinutesLoggedThisWeekFromMonday()
    {
        var (userClient, _) = await factory.CreateUserClientAsync();
        using var user = userClient;
        var project = await factory.CreateProjectAsync(user);
        var task = await user.CreateTaskAsync(project.Id, new { title = "Write copy" });
        var monday = Today.AddDays(-(((int)Today.DayOfWeek + 6) % 7));
        await PostAsync(user, project.Id, task.Id, Today, 60, null);
        await PostAsync(user, project.Id, task.Id, monday, 15, null);
        await PostAsync(user, project.Id, task.Id, monday.AddDays(-1), 99, null);

        var dashboard = await user.GetJsonAsync<WeekDashboardResponse>("/api/dashboard");

        var expected = new int[7];
        expected[Today.DayNumber - monday.DayNumber] += 60;
        expected[0] += 15;
        dashboard.WeekMinutes.ShouldBe(75);
        dashboard.WeekMinutesByDay.ShouldBe(expected);
    }

    private static string EntriesUrl(Guid projectId, Guid taskId) => $"/api/projects/{projectId}/tasks/{taskId}/time-entries";

    private static string EntryUrl(Guid projectId, Guid taskId, Guid entryId) => $"{EntriesUrl(projectId, taskId)}/{entryId}";

    private static async Task<EntryResponse> PostAsync(HttpClient client, Guid projectId, Guid taskId, DateOnly date, int minutes, string? note)
    {
        using var response = await client.PostAsJsonAsync(EntriesUrl(projectId, taskId), new { date, minutes, note }, Ct);
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<EntryResponse>();
    }

    private sealed record EntryResponse(Guid Id, Guid TaskId, Guid UserId, string UserName, DateOnly Date, int Minutes, string? Note);

    private sealed record TaskTimeResponse(List<EntryResponse> Entries, int TotalMinutes, int? EstimatedMinutes);

    private sealed record UserRow(Guid UserId, string UserName, int Minutes);

    private sealed record TaskRow(Guid TaskId, string Title, int? EstimatedMinutes, int ActualMinutes);

    private sealed record SummaryResponse(int TotalMinutes, List<UserRow> ByUser, List<TaskRow> ByTask);

    private sealed record WeekDashboardResponse(int WeekMinutes, List<int> WeekMinutesByDay);
}
