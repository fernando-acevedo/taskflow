using System.Net;
using TaskFlow.Api.IntegrationTests.Infrastructure;

namespace TaskFlow.Api.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class DashboardTests(SqlServerApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateOnly Today => factory.Today;

    [Fact]
    public async Task ProjectDashboardCountsByStatusPriorityAndAssignee()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        var (memberClient, memberEmail) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        using var member = memberClient;
        var project = await factory.CreateProjectAsync(owner);
        (await owner.AddMemberAsync(project.Id, memberEmail)).EnsureSuccessStatusCode();
        var memberId = await member.GetMyIdAsync();

        var late = await owner.CreateTaskAsync(project.Id, new { title = "Late", priority = "Critical", assigneeId = memberId, dueDate = Today });
        await owner.CreateTaskAsync(project.Id, new { title = "Soon", priority = "High", assigneeId = memberId, dueDate = Today.AddDays(3) });
        await owner.CreateTaskAsync(project.Id, new { title = "Someday", priority = "Low" });
        var done = await owner.CreateTaskAsync(project.Id, new { title = "Done", priority = "High", assigneeId = memberId });
        await owner.MoveTaskAsync(project.Id, done, "Done", 0);
        // The API refuses past due dates on new tasks, so overdue tasks are made by moving the date afterwards.
        await factory.SetTaskDueDateAsync(late.Id, Today.AddDays(-2));

        var dashboard = await member.GetJsonAsync<ProjectDashboardResponse>($"/api/projects/{project.Id}/dashboard");

        dashboard.TotalTasks.ShouldBe(4);
        dashboard.OpenTasks.ShouldBe(3);
        dashboard.OverdueTasks.ShouldBe(1);
        dashboard.DueThisWeek.ShouldBe(1);
        dashboard.ByStatus.Select(row => (row.Status, row.Count)).ShouldBe([("ToDo", 3), ("InProgress", 0), ("Review", 0), ("Done", 1)]);
        dashboard.OpenByPriority.Select(row => (row.Priority, row.Count)).ShouldBe([("Low", 1), ("Medium", 0), ("High", 1), ("Critical", 1)]);
        dashboard.OpenByAssignee.Select(row => (row.UserId, row.OpenTasks, row.OverdueTasks)).ShouldBe([(memberId, 2, 1), (null, 1, 0)]);
        dashboard.OpenByAssignee[0].DisplayName.ShouldBe(memberEmail.Split('@')[0]);
    }

    [Fact]
    public async Task OutsidersCannotSeeAProjectDashboard()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        var (outsiderClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        using var outsider = outsiderClient;
        var project = await factory.CreateProjectAsync(owner);

        using var response = await outsider.GetAsync(new Uri($"/api/projects/{project.Id}/dashboard", UriKind.Relative), Ct);
        using var unknown = await owner.GetAsync(new Uri($"/api/projects/{Guid.NewGuid()}/dashboard", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MyDashboardListsMyOpenTasksAndMyClients()
    {
        var (userClient, _) = await factory.CreateUserClientAsync();
        using var user = userClient;
        var me = await user.GetMyIdAsync();
        var project = await factory.CreateProjectAsync(user);
        var archivedProject = await factory.CreateProjectAsync(user);

        var overdue = await user.CreateTaskAsync(project.Id, new { title = "Overdue", assigneeId = me, dueDate = Today });
        // The week window is today … today + 6: one task on its last day, one just outside it.
        await user.CreateTaskAsync(project.Id, new { title = "This week", assigneeId = me, dueDate = Today.AddDays(6) });
        await user.CreateTaskAsync(project.Id, new { title = "Next week", assigneeId = me, dueDate = Today.AddDays(7) });
        await user.CreateTaskAsync(project.Id, new { title = "No date", assigneeId = me });
        await user.CreateTaskAsync(project.Id, new { title = "Not mine" });
        await user.CreateTaskAsync(archivedProject.Id, new { title = "In an archived project", assigneeId = me });
        (await user.ArchiveProjectAsync(archivedProject.Id)).EnsureSuccessStatusCode();
        await factory.SetTaskDueDateAsync(overdue.Id, Today.AddDays(-1));

        var dashboard = await user.GetJsonAsync<MyDashboardResponse>("/api/dashboard");

        dashboard.OpenTasks.ShouldBe(4);
        dashboard.OverdueTasks.ShouldBe(1);
        dashboard.DueThisWeek.ShouldBe(1);
        dashboard.Tasks.Select(task => task.Title).ShouldBe(["Overdue", "This week", "Next week", "No date"]);
        dashboard.Tasks.Select(task => task.IsOverdue).ShouldBe([true, false, false, false]);
        dashboard.Tasks[0].ProjectName.ShouldBe(project.Name);
        var client = dashboard.Clients.ShouldHaveSingleItem();
        client.ClientName.ShouldBe(project.ClientName);
        (client.ActiveProjects, client.OpenTasks, client.OverdueTasks).ShouldBe((1, 5, 1));
    }

    private sealed record StatusRow(string Status, int Count);

    private sealed record PriorityRow(string Priority, int Count);

    private sealed record AssigneeRow(Guid? UserId, string? DisplayName, int OpenTasks, int OverdueTasks);

    private sealed record ProjectDashboardResponse(
        int TotalTasks,
        int OpenTasks,
        int OverdueTasks,
        int DueThisWeek,
        List<StatusRow> ByStatus,
        List<PriorityRow> OpenByPriority,
        List<AssigneeRow> OpenByAssignee);

    private sealed record MyTaskRow(Guid Id, string Title, string ProjectName, bool IsOverdue);

    private sealed record ClientRow(string ClientName, int ActiveProjects, int OpenTasks, int OverdueTasks);

    private sealed record MyDashboardResponse(int OpenTasks, int OverdueTasks, int DueThisWeek, List<MyTaskRow> Tasks, List<ClientRow> Clients);
}
