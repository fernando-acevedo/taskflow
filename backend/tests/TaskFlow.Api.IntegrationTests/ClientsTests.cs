using System.Net;
using System.Net.Http.Json;
using TaskFlow.Api.IntegrationTests.Infrastructure;

namespace TaskFlow.Api.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class ClientsTests(SqlServerApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StandardUserCannotReadClientDetailsOrCreateClients()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var created = await admin.CreateClientAsync();
        var (userClient, _) = await factory.CreateUserClientAsync();
        using var user = userClient;

        using var list = await user.GetAsync(new Uri($"/api/clients?search={created.Name}", UriKind.Relative), Ct);
        using var get = await user.GetAsync(new Uri($"/api/clients/{created.Id}", UriKind.Relative), Ct);
        using var create = await user.PostAsJsonAsync("/api/clients", new { name = "Not allowed" }, Ct);
        using var archive = await user.ArchiveClientAsync(created.Id);

        list.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        get.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        create.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        archive.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminReadsAClientAndUnknownIdsAreNotFound()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var created = await admin.CreateClientAsync();

        var read = await admin.GetJsonAsync<ClientResponse>($"/api/clients/{created.Id}");
        using var unknown = await admin.GetAsync(new Uri($"/api/clients/{Guid.NewGuid()}", UriKind.Relative), Ct);
        using var archiveUnknown = await admin.ArchiveClientAsync(Guid.NewGuid());

        read.ShouldBe(created);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await unknown.ReadAsync<ProblemResponse>()).Code.ShouldBe("Clients.NotFound");
        archiveUnknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OptionsListEveryActiveClientWithoutContactDetails()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var active = await admin.CreateClientAsync();
        var archived = await admin.CreateClientAsync();
        (await admin.ArchiveClientAsync(archived.Id)).EnsureSuccessStatusCode();
        var (userClient, _) = await factory.CreateUserClientAsync();
        using var user = userClient;

        using var response = await user.GetAsync(new Uri("/api/clients/options", UriKind.Relative), Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        var options = await response.ReadAsync<List<ClientOptionResponse>>();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        options.ShouldContain(new ClientOptionResponse(active.Id, active.Name));
        options.ShouldNotContain(option => option.Id == archived.Id);
        body.ShouldNotContain("contactEmail");
    }

    [Fact]
    public async Task SearchTreatsLikeWildcardsAsPlainText()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var unique = Guid.NewGuid().ToString("N")[..8];
        var underscore = await admin.CreateClientAsync($"{unique} a_b");
        await admin.CreateClientAsync($"{unique} axb");
        var percent = await admin.CreateClientAsync($"{unique} 100%");
        await admin.CreateClientAsync($"{unique} 1000");

        var byUnderscore = await admin.GetJsonAsync<PagedResponse<ClientResponse>>(
            $"/api/clients?search={Uri.EscapeDataString($"{unique} a_b")}");
        var byPercent = await admin.GetJsonAsync<PagedResponse<ClientResponse>>(
            $"/api/clients?search={Uri.EscapeDataString($"{unique} 100%")}");

        byUnderscore.Items.ShouldHaveSingleItem().Id.ShouldBe(underscore.Id);
        byPercent.Items.ShouldHaveSingleItem().Id.ShouldBe(percent.Id);
    }

    [Fact]
    public async Task AdminCreatesClientAndNamesAreUnique()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var name = $"Acme {Guid.NewGuid():N}";

        using var first = await admin.PostAsJsonAsync("/api/clients", new { name, contactEmail = "pm@acme.test" }, Ct);
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        var client = await first.ReadAsync<ClientResponse>();
        first.Headers.Location!.AbsolutePath.ShouldBe($"/api/clients/{client.Id}");
        client.Name.ShouldBe(name);
        client.ContactEmail.ShouldBe("pm@acme.test");
        (await admin.GetJsonAsync<ClientResponse>($"/api/clients/{client.Id}")).ShouldBe(client);

        using var duplicate = await admin.PostAsJsonAsync("/api/clients", new { name = $" {name} " }, Ct);
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await duplicate.ReadAsync<ProblemResponse>()).Code.ShouldBe("Clients.NameTaken");
    }

    [Fact]
    public async Task InvalidEmailIsAFieldError()
    {
        using var admin = await factory.CreateAdminClientAsync();

        using var response = await admin.PostAsJsonAsync(
            "/api/clients", new { name = $"Acme {Guid.NewGuid():N}", contactEmail = "not-an-email" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadAsync<ProblemResponse>()).Errors!.Keys.ShouldBe(["contactEmail"]);
    }

    [Fact]
    public async Task ClientWithActiveProjectsCannotBeArchived()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var client = await admin.CreateClientAsync();
        var project = await admin.CreateProjectAsync(client.Id);

        using var blocked = await admin.ArchiveClientAsync(client.Id);
        blocked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await blocked.ReadAsync<ProblemResponse>()).Code.ShouldBe("Clients.HasActiveProjects");

        using var archiveProject = await admin.ArchiveProjectAsync(project.Id);
        archiveProject.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var archived = await admin.ArchiveClientAsync(client.Id);
        archived.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetJsonAsync<ClientResponse>($"/api/clients/{client.Id}")).IsArchived.ShouldBeTrue();

        using var restoreProject = await admin.RestoreProjectAsync(project.Id);
        restoreProject.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await restoreProject.ReadAsync<ProblemResponse>()).Code.ShouldBe("Clients.Archived");
    }
}
