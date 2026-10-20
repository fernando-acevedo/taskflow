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
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? string.Empty);
        using var admin = await factory.CreateAdminClientAsync();
        var created = await admin.CreateClientAsync();
        var (userClient, _) = await factory.CreateUserClientAsync();
        using var user = userClient;

        using var list = await user.GetAsync(new Uri($"/api/clients?search={created.Name}", UriKind.Relative), Ct);
        using var get = await user.GetAsync(new Uri($"/api/clients/{created.Id}", UriKind.Relative), Ct);
        using var create = await user.PostAsJsonAsync("/api/clients", new { name = "Not allowed" }, Ct);

        list.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        get.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        create.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OptionsListEveryActiveClientWithoutContactDetails()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? string.Empty);
        using var admin = await factory.CreateAdminClientAsync();
        var active = await admin.CreateClientAsync();
        var archived = await admin.CreateClientAsync();
        (await admin.PostAsync(new Uri($"/api/clients/{archived.Id}/archive", UriKind.Relative), null, Ct)).EnsureSuccessStatusCode();
        var (userClient, _) = await factory.CreateUserClientAsync();
        using var user = userClient;

        using var response = await user.GetAsync(new Uri("/api/clients/options", UriKind.Relative), Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        var options = await response.Content.ReadFromJsonAsync<List<ClientResponse>>(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        options.ShouldNotBeNull();
        options.ShouldContain(option => option.Id == active.Id && option.Name == active.Name);
        options.ShouldNotContain(option => option.Id == archived.Id);
        body.ShouldNotContain("contactEmail");
    }

    [Fact]
    public async Task SearchTreatsLikeWildcardsAsPlainText()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? string.Empty);
        using var admin = await factory.CreateAdminClientAsync();
        var unique = Guid.NewGuid().ToString("N")[..8];
        var underscore = await admin.CreateClientAsync($"{unique} a_b");
        await admin.CreateClientAsync($"{unique} axb");
        var percent = await admin.CreateClientAsync($"{unique} 100%");
        await admin.CreateClientAsync($"{unique} 1000");

        var byUnderscore = await admin.GetFromJsonAsync<PagedResponse<ClientResponse>>(
            $"/api/clients?search={Uri.EscapeDataString($"{unique} a_b")}", Ct);
        var byPercent = await admin.GetFromJsonAsync<PagedResponse<ClientResponse>>(
            $"/api/clients?search={Uri.EscapeDataString($"{unique} 100%")}", Ct);

        byUnderscore!.Items.ShouldHaveSingleItem().Id.ShouldBe(underscore.Id);
        byPercent!.Items.ShouldHaveSingleItem().Id.ShouldBe(percent.Id);
    }

    [Fact]
    public async Task AdminCreatesClientAndNamesAreUnique()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? string.Empty);
        using var admin = await factory.CreateAdminClientAsync();
        var name = $"Acme {Guid.NewGuid():N}";

        using var first = await admin.PostAsJsonAsync("/api/clients", new { name, contactEmail = "pm@acme.test" }, Ct);
        using var duplicate = await admin.PostAsJsonAsync("/api/clients", new { name = $" {name} " }, Ct);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        var client = await first.Content.ReadFromJsonAsync<ClientResponse>(Ct);
        first.Headers.Location!.AbsolutePath.ShouldBe($"/api/clients/{client!.Id}");
        client.ContactEmail.ShouldBe("pm@acme.test");
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await duplicate.Content.ReadFromJsonAsync<ProblemResponse>(Ct))!.Code.ShouldBe("Clients.NameTaken");
    }

    [Fact]
    public async Task InvalidEmailIsAFieldError()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? string.Empty);
        using var admin = await factory.CreateAdminClientAsync();

        using var response = await admin.PostAsJsonAsync("/api/clients", new { name = "Acme", contactEmail = "not-an-email" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ProblemResponse>(Ct))!.Errors!.Keys.ShouldBe(["contactEmail"]);
    }

    [Fact]
    public async Task ClientWithActiveProjectsCannotBeArchived()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? string.Empty);
        using var admin = await factory.CreateAdminClientAsync();
        var client = await admin.CreateClientAsync();
        var project = await admin.CreateProjectAsync(client.Id);

        using var blocked = await admin.PostAsync(new Uri($"/api/clients/{client.Id}/archive", UriKind.Relative), null, Ct);
        using var archiveProject = await admin.PostAsync(new Uri($"/api/projects/{project.Id}/archive", UriKind.Relative), null, Ct);
        using var archived = await admin.PostAsync(new Uri($"/api/clients/{client.Id}/archive", UriKind.Relative), null, Ct);
        using var restoreProject = await admin.PostAsync(new Uri($"/api/projects/{project.Id}/restore", UriKind.Relative), null, Ct);

        blocked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await blocked.Content.ReadFromJsonAsync<ProblemResponse>(Ct))!.Code.ShouldBe("Clients.HasActiveProjects");
        archiveProject.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        archived.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        restoreProject.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await restoreProject.Content.ReadFromJsonAsync<ProblemResponse>(Ct))!.Code.ShouldBe("Clients.Archived");
    }
}
