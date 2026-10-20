using System.Net;
using System.Net.Http.Json;
using TaskFlow.Api.IntegrationTests.Infrastructure;

namespace TaskFlow.Api.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class ProjectsTests(SqlServerApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreatorOwnsTheProjectAndOthersCannotSeeIt()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? string.Empty);
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        var (outsiderClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        using var outsider = outsiderClient;

        var project = await factory.CreateProjectAsync(owner);
        var ownList = await owner.GetFromJsonAsync<PagedResponse<ProjectResponse>>("/api/projects", Ct);
        var outsiderList = await outsider.GetFromJsonAsync<PagedResponse<ProjectResponse>>("/api/projects", Ct);
        using var outsiderGet = await outsider.GetAsync(new Uri($"/api/projects/{project.Id}", UriKind.Relative), Ct);

        project.MyRole.ShouldBe("Owner");
        project.MemberCount.ShouldBe(1);
        ownList!.Items.ShouldHaveSingleItem().Id.ShouldBe(project.Id);
        outsiderList!.TotalCount.ShouldBe(0);
        outsiderGet.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await outsiderGet.Content.ReadFromJsonAsync<ProblemResponse>(Ct))!.Code.ShouldBe("Projects.Forbidden");
    }

    [Fact]
    public async Task AdminCanReadAnyProjectButNotWorkInIt()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? string.Empty);
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        using var admin = await factory.CreateAdminClientAsync();
        var project = await factory.CreateProjectAsync(owner);

        var viewed = await admin.GetFromJsonAsync<ProjectResponse>($"/api/projects/{project.Id}", Ct);
        using var createTask = await admin.PostAsJsonAsync($"/api/projects/{project.Id}/tasks", new { title = "Nope" }, Ct);

        viewed!.MyRole.ShouldBeNull();
        createTask.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OwnerManagesMembersAndRemovedMembersLoseAccess()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? string.Empty);
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        var (memberClient, memberEmail) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        using var member = memberClient;
        var project = await factory.CreateProjectAsync(owner);

        using var add = await owner.AddMemberAsync(project.Id, memberEmail);
        using var addAgain = await owner.AddMemberAsync(project.Id, memberEmail);
        var memberId = await member.GetMyIdAsync();
        using var memberAddsSomeone = await member.AddMemberAsync(project.Id, AuthApi.NewEmail());
        var members = await member.GetFromJsonAsync<List<MemberResponse>>($"/api/projects/{project.Id}/members", Ct);

        add.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await add.Content.ReadFromJsonAsync<MemberResponse>(Ct))!.Role.ShouldBe("Member");
        addAgain.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        memberAddsSomeone.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        members!.Select(m => m.Role).ShouldBe(["Owner", "Member"]);

        using var remove = await owner.DeleteAsync(new Uri($"/api/projects/{project.Id}/members/{memberId}", UriKind.Relative), Ct);
        using var afterRemoval = await member.GetAsync(new Uri($"/api/projects/{project.Id}", UriKind.Relative), Ct);

        remove.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        afterRemoval.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("en", "No user is registered with that email.")]
    [InlineData("es", "No hay ningún usuario registrado con ese email.")]
    public async Task AddingAnUnknownEmailIsNotFound(string language, string expectedTitle)
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? string.Empty);
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        owner.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);

        using var response = await owner.AddMemberAsync(project.Id, AuthApi.NewEmail());
        var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        problem!.Code.ShouldBe("Users.EmailNotFound");
        problem.Title.ShouldBe(expectedTitle);
    }

    [Fact]
    public async Task LastOwnerCannotLeaveUntilOwnershipIsTransferred()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? string.Empty);
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        var (memberClient, memberEmail) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        using var member = memberClient;
        var project = await factory.CreateProjectAsync(owner);
        (await owner.AddMemberAsync(project.Id, memberEmail)).EnsureSuccessStatusCode();
        var ownerId = await owner.GetMyIdAsync();
        var memberId = await member.GetMyIdAsync();
        var leaveUri = new Uri($"/api/projects/{project.Id}/members/{ownerId}", UriKind.Relative);

        using var blocked = await owner.DeleteAsync(leaveUri, Ct);
        using var promote = await owner.PutAsJsonAsync($"/api/projects/{project.Id}/members/{memberId}", new { role = "Owner" }, Ct);
        using var leave = await owner.DeleteAsync(leaveUri, Ct);

        blocked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await blocked.Content.ReadFromJsonAsync<ProblemResponse>(Ct))!.Code.ShouldBe("Projects.LastOwner");
        promote.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        leave.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.GetFromJsonAsync<ProjectResponse>($"/api/projects/{project.Id}", Ct))!.MyRole.ShouldBe("Owner");
    }

    [Fact]
    public async Task UpdateWithAStaleRowVersionConflicts()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? string.Empty);
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        var uri = $"/api/projects/{project.Id}";

        using var first = await owner.PutAsJsonAsync(uri, new { project.ClientId, name = "First edit", project.RowVersion }, Ct);
        using var second = await owner.PutAsJsonAsync(uri, new { project.ClientId, name = "Second edit", project.RowVersion }, Ct);

        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await second.Content.ReadFromJsonAsync<ProblemResponse>(Ct))!.Code.ShouldBe("Concurrency.Stale");
        (await owner.GetFromJsonAsync<ProjectResponse>(uri, Ct))!.Name.ShouldBe("First edit");
    }

    [Fact]
    public async Task ArchivedProjectIsReadOnlyAndListedSeparately()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? string.Empty);
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);

        using var archive = await owner.PostAsync(new Uri($"/api/projects/{project.Id}/archive", UriKind.Relative), null, Ct);
        using var createTask = await owner.PostAsJsonAsync($"/api/projects/{project.Id}/tasks", new { title = "Too late" }, Ct);
        var active = await owner.GetFromJsonAsync<PagedResponse<ProjectResponse>>("/api/projects", Ct);
        var archived = await owner.GetFromJsonAsync<PagedResponse<ProjectResponse>>("/api/projects?archived=true", Ct);

        archive.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        createTask.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await createTask.Content.ReadFromJsonAsync<ProblemResponse>(Ct))!.Code.ShouldBe("Projects.Archived");
        active!.TotalCount.ShouldBe(0);
        archived!.Items.ShouldHaveSingleItem().IsArchived.ShouldBeTrue();
    }
}
