using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Api.IntegrationTests.Infrastructure;
using TaskFlow.Application.Notifications;
using TaskFlow.Infrastructure.Email;
using TaskFlow.Infrastructure.Tenancy;

namespace TaskFlow.Api.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class NotificationTests(SqlServerApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AssigningATaskNotifiesTheAssigneeButNeverTheActor()
    {
        var team = await TeamAsync();
        var projectId = team.Project.Id;

        var assignedToMember = await team.Owner.CreateTaskAsync(projectId, new { title = "Write copy", assigneeId = team.MemberId });
        await team.Owner.CreateTaskAsync(projectId, new { title = "My own", assigneeId = team.OwnerId });

        var forMember = await ListAsync(team.Member);
        var notification = forMember.Items.ShouldHaveSingleItem();
        notification.Type.ShouldBe("TaskAssigned");
        notification.ProjectId.ShouldBe(projectId);
        notification.TaskId.ShouldBe(assignedToMember.Id);
        notification.ActorId.ShouldBe(team.OwnerId);
        notification.ReadAt.ShouldBeNull();
        notification.Data.GetProperty("taskTitle").GetString().ShouldBe("Write copy");
        notification.Data.GetProperty("projectName").GetString().ShouldBe(team.Project.Name);
        notification.Data.GetProperty("actorName").GetString().ShouldNotBeNullOrWhiteSpace();
        (await ListAsync(team.Owner)).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task ReassigningNotifiesTheNewAssigneeOnly()
    {
        var team = await TeamAsync();
        var task = await team.Owner.CreateTaskAsync(team.Project.Id, new { title = "Write copy", assigneeId = team.MemberId });

        // The member hands the task to the owner: the owner is told, the member (the actor) is not told twice.
        using var response = await team.Member.PutAsJsonAsync(
            $"/api/projects/{team.Project.Id}/tasks/{task.Id}",
            new { title = "Write copy", priority = "Medium", assigneeId = team.OwnerId, rowVersion = task.RowVersion },
            Ct);
        response.EnsureSuccessStatusCode();

        (await ListAsync(team.Owner)).Items.ShouldHaveSingleItem().Type.ShouldBe("TaskAssigned");
        (await ListAsync(team.Member)).TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task MentioningNotifiesAndEditingOnlyNotifiesTheNewlyMentioned()
    {
        var team = await TeamAsync();
        var task = await team.Owner.CreateTaskAsync(team.Project.Id, new { title = "Write copy" });

        var comment = await PostCommentAsync(team.Owner, team.Project.Id, task.Id, "Look at this", [team.MemberId]);
        var mention = (await ListAsync(team.Member)).Items.ShouldHaveSingleItem();
        mention.Type.ShouldBe("Mentioned");
        mention.TaskId.ShouldBe(task.Id);
        mention.Data.GetProperty("commentId").GetGuid().ShouldBe(comment.Id);

        using var edit = await team.Owner.PutAsJsonAsync(
            $"/api/projects/{team.Project.Id}/tasks/{task.Id}/comments/{comment.Id}",
            new { body = "Look at this again", mentionedUserIds = new[] { team.MemberId, team.ThirdId }, rowVersion = comment.RowVersion },
            Ct);
        edit.EnsureSuccessStatusCode();

        (await ListAsync(team.Member)).TotalCount.ShouldBe(1);
        (await ListAsync(team.Third)).Items.ShouldHaveSingleItem().Type.ShouldBe("Mentioned");
        (await ListAsync(team.Owner)).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task CommentingNotifiesTheAssigneeWithoutDuplicatingAMention()
    {
        var team = await TeamAsync();
        var task = await team.Owner.CreateTaskAsync(team.Project.Id, new { title = "Write copy", assigneeId = team.MemberId });

        await PostCommentAsync(team.Owner, team.Project.Id, task.Id, "Plain comment", null);
        await PostCommentAsync(team.Owner, team.Project.Id, task.Id, "With mention", [team.MemberId]);
        await PostCommentAsync(team.Member, team.Project.Id, task.Id, "My own comment", null);

        var types = (await ListAsync(team.Member)).Items.Select(item => item.Type).Order().ToList();
        types.ShouldBe(["CommentOnYourTask", "Mentioned", "TaskAssigned"]);
        (await ListAsync(team.Owner)).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task TheDailyNoticeIsIdempotentAndSkipsDoneTasks()
    {
        var team = await TeamAsync();
        var tomorrow = factory.Today.AddDays(1);
        var due = await team.Owner.CreateTaskAsync(
            team.Project.Id, new { title = "Due tomorrow", assigneeId = team.MemberId, dueDate = tomorrow });
        var done = await team.Owner.CreateTaskAsync(
            team.Project.Id, new { title = "Already done", assigneeId = team.MemberId, dueDate = tomorrow });
        await team.Owner.CreateTaskAsync(team.Project.Id, new { title = "Far away", assigneeId = team.MemberId, dueDate = factory.Today.AddDays(5) });
        await team.Owner.MoveTaskAsync(team.Project.Id, done, "Done", 0);

        var first = await RunDueSoonAsync();
        var second = await RunDueSoonAsync();

        first.ShouldBeGreaterThanOrEqualTo(1);
        second.ShouldBe(0);
        var dueSoon = (await ListAsync(team.Member)).Items.Where(item => item.Type == "TaskDueSoon").ToList();
        var notification = dueSoon.ShouldHaveSingleItem();
        notification.TaskId.ShouldBe(due.Id);
        notification.ActorId.ShouldBeNull();
        notification.Data.GetProperty("dueDate").GetString().ShouldBe(tomorrow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task ADueDateThatMovesIsNotifiedAgain()
    {
        var team = await TeamAsync();
        var task = await team.Owner.CreateTaskAsync(
            team.Project.Id, new { title = "Due soon", assigneeId = team.MemberId, dueDate = factory.Today });
        await RunDueSoonAsync();

        await factory.SetTaskDueDateAsync(task.Id, factory.Today.AddDays(1));
        await RunDueSoonAsync();

        (await ListAsync(team.Member)).Items.Count(item => item.Type == "TaskDueSoon").ShouldBe(2);
    }

    [Fact]
    public async Task EmailIsSentOnlyWhenThePreferenceIsOn()
    {
        var team = await TeamAsync();
        var projectId = team.Project.Id;

        await team.Owner.CreateTaskAsync(projectId, new { title = "No email yet", assigneeId = team.MemberId });
        NotificationEmails(team.MemberEmail).ShouldBeEmpty();

        (await GetPreferencesAsync(team.Member)).ShouldBe(new PreferencesResponse(false, false, false, false));
        using var saved = await team.Member.PutAsJsonAsync(
            "/api/notifications/preferences",
            new { emailOnAssigned = true, emailOnMentioned = false, emailOnCommentOnMyTask = false, emailDailyDigest = false },
            Ct);
        saved.EnsureSuccessStatusCode();
        var task = await team.Owner.CreateTaskAsync(projectId, new { title = "Email me", assigneeId = team.MemberId });
        await PostCommentAsync(team.Owner, projectId, task.Id, "Mention is off", [team.MemberId]);

        var sent = NotificationEmails(team.MemberEmail).ShouldHaveSingleItem();
        sent.TextBody.ShouldContain("Email me");
        sent.TextBody.ShouldContain($"/projects/{projectId}/board?task={task.Id}");
        (await GetPreferencesAsync(team.Member)).EmailOnAssigned.ShouldBeTrue();
        (await ListAsync(team.Member)).TotalCount.ShouldBe(3);
    }

    [Fact]
    public async Task TheDailyDigestIsOneEmailWithEveryTask()
    {
        var team = await TeamAsync();
        using var saved = await team.Member.PutAsJsonAsync(
            "/api/notifications/preferences",
            new { emailOnAssigned = false, emailOnMentioned = false, emailOnCommentOnMyTask = false, emailDailyDigest = true },
            Ct);
        saved.EnsureSuccessStatusCode();
        await team.Owner.CreateTaskAsync(team.Project.Id, new { title = "First due", assigneeId = team.MemberId, dueDate = factory.Today });
        await team.Owner.CreateTaskAsync(team.Project.Id, new { title = "Second due", assigneeId = team.MemberId, dueDate = factory.Today.AddDays(1) });

        await RunDueSoonAsync();
        await RunDueSoonAsync();

        var sent = NotificationEmails(team.MemberEmail).ShouldHaveSingleItem();
        sent.TextBody.ShouldContain("First due");
        sent.TextBody.ShouldContain("Second due");
    }

    [Fact]
    public async Task UnreadCountReadAndReadAllUpdateTheNotifications()
    {
        var team = await TeamAsync();
        for (var index = 0; index < 3; index++)
        {
            await team.Owner.CreateTaskAsync(team.Project.Id, new { title = $"Task {index}", assigneeId = team.MemberId });
        }

        (await UnreadCountAsync(team.Member)).ShouldBe(3);
        var items = (await ListAsync(team.Member)).Items;
        using var read = await team.Member.PostAsync(new Uri($"/api/notifications/{items[0].Id}/read", UriKind.Relative), null, Ct);
        read.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var again = await team.Member.PostAsync(new Uri($"/api/notifications/{items[0].Id}/read", UriKind.Relative), null, Ct);
        again.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await UnreadCountAsync(team.Member)).ShouldBe(2);
        var unread = await team.Member.GetJsonAsync<PagedResponse<NotificationResponse>>("/api/notifications?unreadOnly=true");
        unread.TotalCount.ShouldBe(2);
        unread.Items.ShouldAllBe(item => item.ReadAt == null);
        (await ListAsync(team.Member)).Items.Single(item => item.Id == items[0].Id).ReadAt.ShouldNotBeNull();

        using var all = await team.Member.PostAsync(new Uri("/api/notifications/read-all", UriKind.Relative), null, Ct);
        all.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await UnreadCountAsync(team.Member)).ShouldBe(0);
        (await ListAsync(team.Member)).TotalCount.ShouldBe(3);
    }

    [Fact]
    public async Task ListIsPagedNewestFirst()
    {
        var team = await TeamAsync();
        for (var index = 0; index < 3; index++)
        {
            factory.Time.Advance(TimeSpan.FromMinutes(1));
            await team.Owner.CreateTaskAsync(team.Project.Id, new { title = $"Task {index}", assigneeId = team.MemberId });
        }

        var page = await team.Member.GetJsonAsync<PagedResponse<NotificationResponse>>("/api/notifications?page=1&pageSize=2");

        page.TotalCount.ShouldBe(3);
        page.Items.Select(item => item.Data.GetProperty("taskTitle").GetString()).ShouldBe(["Task 2", "Task 1"]);
        using var invalid = await team.Member.GetAsync(new Uri("/api/notifications?pageSize=1000", UriKind.Relative), Ct);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task NobodyReadsAnotherUsersNotifications()
    {
        var team = await TeamAsync();
        await team.Owner.CreateTaskAsync(team.Project.Id, new { title = "Private", assigneeId = team.MemberId });
        var notification = (await ListAsync(team.Member)).Items.ShouldHaveSingleItem();

        using var response = await team.Third.PostAsync(new Uri($"/api/notifications/{notification.Id}/read", UriKind.Relative), null, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.ReadAsync<ProblemResponse>()).Code.ShouldBe("Notifications.NotFound");
        (await ListAsync(team.Third)).TotalCount.ShouldBe(0);
        (await UnreadCountAsync(team.Member)).ShouldBe(1);
    }

    [Fact]
    public async Task NotificationsOfAProjectTheUserLeftAreHidden()
    {
        var team = await TeamAsync();
        await team.Owner.CreateTaskAsync(team.Project.Id, new { title = "Secret", assigneeId = team.MemberId });
        (await ListAsync(team.Member)).TotalCount.ShouldBe(1);
        (await UnreadCountAsync(team.Member)).ShouldBe(1);

        using var removed = await team.Owner.DeleteAsync(new Uri($"/api/projects/{team.Project.Id}/members/{team.MemberId}", UriKind.Relative), Ct);
        removed.EnsureSuccessStatusCode();

        (await ListAsync(team.Member)).TotalCount.ShouldBe(0);
        (await UnreadCountAsync(team.Member)).ShouldBe(0);
    }

    [Fact]
    public async Task NotificationEndpointsRequireSignIn()
    {
        using var anonymous = factory.CreateClient();

        using var response = await anonymous.GetAsync(new Uri("/api/notifications/unread-count", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<Team> TeamAsync()
    {
        var (owner, _) = await factory.CreateUserClientAsync();
        var (member, memberEmail) = await factory.CreateUserClientAsync();
        var (third, thirdEmail) = await factory.CreateUserClientAsync();
        var project = await factory.CreateProjectAsync(owner);
        (await owner.AddMemberAsync(project.Id, memberEmail)).EnsureSuccessStatusCode();
        (await owner.AddMemberAsync(project.Id, thirdEmail)).EnsureSuccessStatusCode();
        return new Team(
            owner, member, third, project, memberEmail,
            await owner.GetMyIdAsync(), await member.GetMyIdAsync(), await third.GetMyIdAsync());
    }

    /// <summary>The emails to <paramref name="email"/> about tasks, leaving out the confirmation sent at registration.</summary>
    private IReadOnlyList<EmailMessage> NotificationEmails(string email) =>
        [.. factory.Emails.SentTo(email).Where(message => !message.Subject.Contains("Confirm", StringComparison.Ordinal))];

    private Task<int> RunDueSoonAsync(Guid? tenantId = null) => RunDueSoonInAsync(tenantId ?? TenantDefaults.DefaultId);

    /// <summary>Runs the daily job for one tenant, the way the background service does (ADR 0014).</summary>
    private async Task<int> RunDueSoonInAsync(Guid tenantId)
    {
        var scope = factory.Services.GetRequiredService<TenantScope>();
        return await scope.RunAsync(
            tenantId,
            async (services, token) =>
            {
                var result = await services.GetRequiredService<NotifyDueSoonHandler>().HandleAsync(token);
                result.IsSuccess.ShouldBeTrue();
                return result.Value;
            },
            Ct);
    }

    private static Task<PagedResponse<NotificationResponse>> ListAsync(HttpClient client) =>
        client.GetJsonAsync<PagedResponse<NotificationResponse>>("/api/notifications");

    private static async Task<int> UnreadCountAsync(HttpClient client) =>
        (await client.GetJsonAsync<UnreadCountResponse>("/api/notifications/unread-count")).Count;

    private static Task<PreferencesResponse> GetPreferencesAsync(HttpClient client) =>
        client.GetJsonAsync<PreferencesResponse>("/api/notifications/preferences");

    private static async Task<CommentResponse> PostCommentAsync(HttpClient client, Guid projectId, Guid taskId, string body, Guid[]? mentions)
    {
        using var response = await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/tasks/{taskId}/comments", new { body, mentionedUserIds = mentions }, Ct);
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<CommentResponse>();
    }

    private sealed record Team(
        HttpClient Owner,
        HttpClient Member,
        HttpClient Third,
        ProjectResponse Project,
        string MemberEmail,
        Guid OwnerId,
        Guid MemberId,
        Guid ThirdId);

    private sealed record CommentResponse(Guid Id, byte[] RowVersion);

    private sealed record UnreadCountResponse(int Count);

    private sealed record PreferencesResponse(
        bool EmailOnAssigned, bool EmailOnMentioned, bool EmailOnCommentOnMyTask, bool EmailDailyDigest);

    private sealed record NotificationResponse(
        Guid Id,
        string Type,
        Guid ProjectId,
        Guid? TaskId,
        Guid? ActorId,
        JsonElement Data,
        DateTimeOffset CreatedAt,
        DateTimeOffset? ReadAt);
}
