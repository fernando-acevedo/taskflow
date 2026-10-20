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
        return (await response.Content.ReadFromJsonAsync<ClientResponse>(Ct))!;
    }

    public static async Task<ProjectResponse> CreateProjectAsync(this HttpClient client, Guid clientId, string name = "Website redesign")
    {
        using var response = await client.PostAsJsonAsync("/api/projects", new { clientId, name }, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectResponse>(Ct))!;
    }

    /// <summary>Creates a client as the admin and a project in it as <paramref name="owner"/>.</summary>
    public static async Task<ProjectResponse> CreateProjectAsync(this SqlServerApiFactory factory, HttpClient owner)
    {
        using var admin = await factory.CreateAdminClientAsync();
        var client = await admin.CreateClientAsync();
        return await owner.CreateProjectAsync(client.Id);
    }

    /// <summary>Posts <paramref name="task"/> (at least a <c>title</c>) and returns the created task.</summary>
    public static async Task<TaskResponse> CreateTaskAsync(this HttpClient client, Guid projectId, object task)
    {
        using var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/tasks", task, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TaskResponse>(Ct))!;
    }

    public static async Task<HttpResponseMessage> AddMemberAsync(this HttpClient owner, Guid projectId, string email, string role = "Member") =>
        await owner.PostAsJsonAsync($"/api/projects/{projectId}/members", new { email, role }, Ct);

    public static async Task<Guid> GetMyIdAsync(this HttpClient client) =>
        (await client.GetFromJsonAsync<UserResponse>("/api/users/me", Ct))!.Id;
}

public sealed record ClientResponse(Guid Id, string Name, string? ContactEmail, bool IsArchived, int ActiveProjectCount);

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
