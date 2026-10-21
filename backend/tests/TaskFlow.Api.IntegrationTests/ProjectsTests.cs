using System.Net;
using System.Net.Http.Json;
using TaskFlow.Api.IntegrationTests.Infrastructure;

namespace TaskFlow.Api.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class ProjectsTests(SqlServerApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreatingAProjectReturnsItsLocationAndValidatesTheClient()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        using var admin = await factory.CreateAdminClientAsync();
        var client = await admin.CreateClientAsync();
        var archivedClient = await admin.CreateClientAsync();
        (await admin.ArchiveClientAsync(archivedClient.Id)).EnsureSuccessStatusCode();

        using var created = await owner.PostAsJsonAsync("/api/projects", new { clientId = client.Id, name = "Brand refresh" }, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var project = await created.ReadAsync<ProjectResponse>();
        created.Headers.Location!.AbsolutePath.ShouldBe($"/api/projects/{project.Id}");
        (project.Name, project.ClientId, project.ClientName, project.IsArchived).ShouldBe(("Brand refresh", client.Id, client.Name, false));

        using var duplicate = await owner.PostAsJsonAsync("/api/projects", new { clientId = client.Id, name = " Brand refresh " }, Ct);
        using var noName = await owner.PostAsJsonAsync("/api/projects", new { clientId = client.Id, name = "" }, Ct);
        using var unknownClient = await owner.PostAsJsonAsync("/api/projects", new { clientId = Guid.NewGuid(), name = "Nowhere" }, Ct);
        using var forArchivedClient = await owner.PostAsJsonAsync("/api/projects", new { clientId = archivedClient.Id, name = "Too late" }, Ct);

        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await duplicate.ReadAsync<ProblemResponse>()).Code.ShouldBe("Projects.NameTaken");
        noName.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await noName.ReadAsync<ProblemResponse>()).Errors!.Keys.ShouldBe(["name"]);
        unknownClient.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await unknownClient.ReadAsync<ProblemResponse>()).Code.ShouldBe("Clients.NotFound");
        forArchivedClient.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await forArchivedClient.ReadAsync<ProblemResponse>()).Code.ShouldBe("Clients.Archived");
    }

    [Fact]
    public async Task CreatorOwnsTheProjectAndOthersCannotSeeIt()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        var (outsiderClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        using var outsider = outsiderClient;

        var project = await factory.CreateProjectAsync(owner);
        var ownList = await owner.GetJsonAsync<PagedResponse<ProjectResponse>>("/api/projects");
        var outsiderList = await outsider.GetJsonAsync<PagedResponse<ProjectResponse>>("/api/projects");
        using var outsiderGet = await outsider.GetAsync(new Uri($"/api/projects/{project.Id}", UriKind.Relative), Ct);

        project.MyRole.ShouldBe("Owner");
        project.MemberCount.ShouldBe(1);
        ownList.Items.ShouldHaveSingleItem().Id.ShouldBe(project.Id);
        outsiderList.TotalCount.ShouldBe(0);
        outsiderGet.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await outsiderGet.ReadAsync<ProblemResponse>()).Code.ShouldBe("Projects.Forbidden");
    }

    [Fact]
    public async Task UnknownProjectIsNotFound()
    {
        var (userClient, _) = await factory.CreateUserClientAsync();
        using var user = userClient;

        using var response = await user.GetAsync(new Uri($"/api/projects/{Guid.NewGuid()}", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.ReadAsync<ProblemResponse>()).Code.ShouldBe("Projects.NotFound");
    }

    [Fact]
    public async Task AdminCanReadAnyProjectButNotWorkInIt()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        using var admin = await factory.CreateAdminClientAsync();
        var project = await factory.CreateProjectAsync(owner);

        var viewed = await admin.GetJsonAsync<ProjectResponse>($"/api/projects/{project.Id}");
        using var createTask = await admin.PostAsJsonAsync($"/api/projects/{project.Id}/tasks", new { title = "Nope" }, Ct);

        viewed.Id.ShouldBe(project.Id);
        viewed.MyRole.ShouldBeNull();
        createTask.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MemberCanWorkOnTasksButCannotManageTheProject()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        var (memberClient, memberEmail) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        using var member = memberClient;
        var project = await factory.CreateProjectAsync(owner);
        (await owner.AddMemberAsync(project.Id, memberEmail)).EnsureSuccessStatusCode();

        var task = await member.CreateTaskAsync(project.Id, new { title = "By a member" });
        var moved = await member.MoveTaskAsync(project.Id, task, "InProgress", 0);
        using var archive = await member.ArchiveProjectAsync(project.Id);
        using var rename = await member.PutAsJsonAsync(
            $"/api/projects/{project.Id}", new { project.ClientId, name = "Renamed", project.RowVersion }, Ct);

        moved.Status.ShouldBe("InProgress");
        archive.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        rename.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var unchanged = await owner.GetJsonAsync<ProjectResponse>($"/api/projects/{project.Id}");
        unchanged.IsArchived.ShouldBeFalse();
        unchanged.Name.ShouldBe(project.Name);
    }

    [Fact]
    public async Task OwnerManagesMembersAndRemovedMembersLoseAccess()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        var (memberClient, memberEmail) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        using var member = memberClient;
        var project = await factory.CreateProjectAsync(owner);

        using var add = await owner.AddMemberAsync(project.Id, memberEmail);
        add.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await add.ReadAsync<MemberResponse>()).Role.ShouldBe("Member");

        using var addAgain = await owner.AddMemberAsync(project.Id, memberEmail);
        addAgain.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var memberId = await member.GetMyIdAsync();
        using var memberAddsSomeone = await member.AddMemberAsync(project.Id, AuthApi.NewEmail());
        memberAddsSomeone.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var members = await member.GetJsonAsync<List<MemberResponse>>($"/api/projects/{project.Id}/members");
        members.Select(m => m.Role).ShouldBe(["Owner", "Member"]);

        using var remove = await owner.DeleteAsync(new Uri($"/api/projects/{project.Id}/members/{memberId}", UriKind.Relative), Ct);
        remove.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var afterRemoval = await member.GetAsync(new Uri($"/api/projects/{project.Id}", UriKind.Relative), Ct);
        afterRemoval.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("en", "No user is registered with that email.")]
    [InlineData("es", "No hay ningún usuario registrado con ese email.")]
    public async Task AddingAnUnknownEmailIsNotFound(string language, string expectedTitle)
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        owner.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);

        using var response = await owner.AddMemberAsync(project.Id, AuthApi.NewEmail());
        var problem = await response.ReadAsync<ProblemResponse>();

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        problem.Code.ShouldBe("Users.EmailNotFound");
        problem.Title.ShouldBe(expectedTitle);
    }

    [Fact]
    public async Task LastOwnerCannotLeaveUntilOwnershipIsTransferred()
    {
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
        blocked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await blocked.ReadAsync<ProblemResponse>()).Code.ShouldBe("Projects.LastOwner");

        using var promote = await owner.PutAsJsonAsync($"/api/projects/{project.Id}/members/{memberId}", new { role = "Owner" }, Ct);
        promote.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var leave = await owner.DeleteAsync(leaveUri, Ct);
        leave.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.GetJsonAsync<ProjectResponse>($"/api/projects/{project.Id}")).MyRole.ShouldBe("Owner");
    }

    [Fact]
    public async Task UpdateWithAStaleRowVersionConflicts()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        var uri = $"/api/projects/{project.Id}";

        using var first = await owner.PutAsJsonAsync(uri, new { project.ClientId, name = "First edit", project.RowVersion }, Ct);
        using var second = await owner.PutAsJsonAsync(uri, new { project.ClientId, name = "Second edit", project.RowVersion }, Ct);

        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await second.ReadAsync<ProblemResponse>()).Code.ShouldBe("Concurrency.Stale");
        (await owner.GetJsonAsync<ProjectResponse>(uri)).Name.ShouldBe("First edit");
    }

    [Fact]
    public async Task ArchivedProjectIsReadOnlyAndListedSeparately()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);

        using var archive = await owner.ArchiveProjectAsync(project.Id);
        archive.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var createTask = await owner.PostAsJsonAsync($"/api/projects/{project.Id}/tasks", new { title = "Too late" }, Ct);
        var active = await owner.GetJsonAsync<PagedResponse<ProjectResponse>>("/api/projects");
        var archived = await owner.GetJsonAsync<PagedResponse<ProjectResponse>>("/api/projects?archived=true");

        createTask.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await createTask.ReadAsync<ProblemResponse>()).Code.ShouldBe("Projects.Archived");
        active.TotalCount.ShouldBe(0);
        archived.Items.ShouldHaveSingleItem().IsArchived.ShouldBeTrue();
    }
}
