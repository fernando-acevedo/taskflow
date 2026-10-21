using System.Net.Http.Json;

namespace TaskFlow.Api.IntegrationTests.Infrastructure;

/// <summary>Helpers to set up clients, projects and tasks through the API.</summary>
public static class ProjectApi
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<ClientResponse> CreateClientAsync(this HttpClient admin, string? name = null)
    {
        using var response = await admin.PostAsJsonAsync("/api/clients", new { name = name ?? $"Client {Guid.NewGuid():N}" }, Ct);
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<ClientResponse>();
    }

    public static Task<HttpResponseMessage> ArchiveClientAsync(this HttpClient admin, Guid clientId) =>
        admin.PostAsync(new Uri($"/api/clients/{clientId}/archive", UriKind.Relative), null, Ct);

    public static async Task<ProjectResponse> CreateProjectAsync(this HttpClient client, Guid clientId, string name = "Website redesign")
    {
        using var response = await client.PostAsJsonAsync("/api/projects", new { clientId, name }, Ct);
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<ProjectResponse>();
    }

    /// <summary>Creates a client as the admin and a project in it as <paramref name="owner"/>.</summary>
    public static async Task<ProjectResponse> CreateProjectAsync(this SqlServerApiFactory factory, HttpClient owner)
    {
        using var admin = await factory.CreateAdminClientAsync();
        var client = await admin.CreateClientAsync();
        return await owner.CreateProjectAsync(client.Id);
    }

    public static Task<HttpResponseMessage> ArchiveProjectAsync(this HttpClient client, Guid projectId) =>
        client.PostAsync(new Uri($"/api/projects/{projectId}/archive", UriKind.Relative), null, Ct);

    public static Task<HttpResponseMessage> RestoreProjectAsync(this HttpClient client, Guid projectId) =>
        client.PostAsync(new Uri($"/api/projects/{projectId}/restore", UriKind.Relative), null, Ct);

    /// <summary>Posts <paramref name="task"/> (at least a <c>title</c>) and returns the created task.</summary>
    public static async Task<TaskResponse> CreateTaskAsync(this HttpClient client, Guid projectId, object task)
    {
        using var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/tasks", task, Ct);
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<TaskResponse>();
    }

    public static Task<HttpResponseMessage> TryMoveTaskAsync(
        this HttpClient client, Guid projectId, TaskResponse task, string status, int position) =>
        client.PutAsJsonAsync($"/api/projects/{projectId}/tasks/{task.Id}/position", new { status, position, task.RowVersion }, Ct);

    public static async Task<TaskResponse> MoveTaskAsync(this HttpClient client, Guid projectId, TaskResponse task, string status, int position)
    {
        using var response = await client.TryMoveTaskAsync(projectId, task, status, position);
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<TaskResponse>();
    }

    public static async Task<HttpResponseMessage> AddMemberAsync(this HttpClient owner, Guid projectId, string email, string role = "Member") =>
        await owner.PostAsJsonAsync($"/api/projects/{projectId}/members", new { email, role }, Ct);

    public static async Task<Guid> GetMyIdAsync(this HttpClient client) =>
        (await client.GetJsonAsync<UserResponse>("/api/users/me")).Id;
}

public sealed record ClientResponse(Guid Id, string Name, string? ContactEmail, bool IsArchived, int ActiveProjectCount);

public sealed record ClientOptionResponse(Guid Id, string Name);

public sealed record ProjectResponse(
    Guid Id,
    string Name,
    Guid ClientId,
    string ClientName,
    bool IsArchived,
    string? MyRole,
    int MemberCount,
    byte[] RowVersion);

public sealed record MemberResponse(Guid UserId, string Email, string Role);

public sealed record TaskResponse(
    Guid Id,
    string Title,
    string Status,
    string Priority,
    Guid? AssigneeId,
    DateOnly? DueDate,
    bool IsOverdue,
    int Position,
    DateTimeOffset? CompletedAt,
    byte[] RowVersion);
