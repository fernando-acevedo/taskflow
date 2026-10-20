using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace TaskFlow.Api.IntegrationTests.Infrastructure;

/// <summary>Helpers around the Identity API Endpoints mapped under /api/auth.</summary>
public static class AuthApi
{
    public const string ValidPassword = "Passw0rd!";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static string NewEmail() => $"user-{Guid.NewGuid():N}@tests.local";

    public static Task<HttpResponseMessage> RegisterAsync(this HttpClient client, string email, string password = ValidPassword) =>
        client.PostAsJsonAsync("/api/auth/register", new { email, password }, Ct);

    public static Task<HttpResponseMessage> LoginAsync(this HttpClient client, string email, string password = ValidPassword) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password }, Ct);

    public static Task<HttpResponseMessage> RefreshAsync(this HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken }, Ct);

    public static async Task<TokenResponse> LoginForTokensAsync(this HttpClient client, string email, string password = ValidPassword)
    {
        using var response = await client.LoginAsync(email, password);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenResponse>(Ct))!;
    }

    /// <summary>Registers a new user and returns a client that sends that user's access token.</summary>
    public static async Task<(HttpClient Client, string Email)> CreateUserClientAsync(this SqlServerApiFactory factory)
    {
        var email = NewEmail();
        var client = factory.CreateClient();
        using (var response = await client.RegisterAsync(email))
        {
            response.EnsureSuccessStatusCode();
        }

        client.UseToken(await client.LoginForTokensAsync(email));
        return (client, email);
    }

    public static async Task<HttpClient> CreateAdminClientAsync(this SqlServerApiFactory factory)
    {
        var client = factory.CreateClient();
        client.UseToken(await client.LoginForTokensAsync(SqlServerApiFactory.AdminEmail, SqlServerApiFactory.AdminPassword));
        return client;
    }

    public static void UseToken(this HttpClient client, TokenResponse tokens) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
}

public sealed record TokenResponse(string TokenType, string AccessToken, long ExpiresIn, string RefreshToken);

public sealed record UserResponse(
    Guid Id,
    string Email,
    string DisplayName,
    bool IsActive,
    IReadOnlyList<string> Roles,
    bool IsInvitationPending = false);

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed record ProblemResponse(int Status, string? Title, string? Detail, string? Code, Dictionary<string, string[]>? Errors);
