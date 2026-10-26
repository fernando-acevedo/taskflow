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

    public static Task<HttpResponseMessage> ConfirmEmailAsync(this HttpClient client, string userId, string code) =>
        client.GetAsync(new Uri($"/api/auth/confirmEmail?userId={Uri.EscapeDataString(userId)}&code={Uri.EscapeDataString(code)}", UriKind.Relative), Ct);

    public static Task<HttpResponseMessage> ResendConfirmationEmailAsync(this HttpClient client, string email) =>
        client.PostAsJsonAsync("/api/auth/resendConfirmationEmail", new { email }, Ct);

    public static Task<HttpResponseMessage> ForgotPasswordAsync(this HttpClient client, string email) =>
        client.PostAsJsonAsync("/api/auth/forgotPassword", new { email }, Ct);

    public static Task<HttpResponseMessage> ResetPasswordAsync(this HttpClient client, string email, string resetCode, string newPassword) =>
        client.PostAsJsonAsync("/api/auth/resetPassword", new { email, resetCode, newPassword }, Ct);

    public static async Task<TokenResponse> LoginForTokensAsync(this HttpClient client, string email, string password = ValidPassword)
    {
        using var response = await client.LoginAsync(email, password);
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<TokenResponse>();
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

/// <summary>Helpers around the admin user endpoints and invitations.</summary>
public static class AdminApi
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static Task<HttpResponseMessage> InviteUserAsync(this HttpClient admin, string email, bool isAdmin = false) =>
        admin.PostAsJsonAsync("/api/admin/users", new { email, displayName = "New Person", isAdmin }, Ct);

    public static Task<HttpResponseMessage> AcceptInvitationAsync(this HttpClient client, string email, string token, string password) =>
        client.PostAsJsonAsync("/api/invitations/accept", new { email, token, password }, Ct);

    public static Task<HttpResponseMessage> ResendInvitationAsync(this HttpClient admin, Guid userId) =>
        admin.PostAsync(new Uri($"/api/admin/users/{userId}/invitation", UriKind.Relative), null, Ct);

    public static Task<HttpResponseMessage> DeactivateUserAsync(this HttpClient admin, Guid userId) =>
        admin.PostAsync(new Uri($"/api/admin/users/{userId}/deactivate", UriKind.Relative), null, Ct);

    public static Task<HttpResponseMessage> ActivateUserAsync(this HttpClient admin, Guid userId) =>
        admin.PostAsync(new Uri($"/api/admin/users/{userId}/activate", UriKind.Relative), null, Ct);

    public static Task<HttpResponseMessage> GrantAdminAsync(this HttpClient admin, Guid userId) =>
        admin.PutAsync(new Uri($"/api/admin/users/{userId}/admin", UriKind.Relative), null, Ct);

    public static Task<HttpResponseMessage> RevokeAdminAsync(this HttpClient admin, Guid userId) =>
        admin.DeleteAsync(new Uri($"/api/admin/users/{userId}/admin", UriKind.Relative), Ct);

    public static async Task<Guid> FindUserIdAsync(this HttpClient admin, string email)
    {
        var page = await admin.GetJsonAsync<PagedResponse<UserResponse>>($"/api/admin/users?search={email}");
        return page.Items.Single().Id;
    }
}

public sealed record TokenResponse(string TokenType, string AccessToken, long ExpiresIn, string RefreshToken);

public sealed record UserResponse(
    Guid Id,
    string Email,
    string DisplayName,
    bool IsActive,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt,
    bool IsInvitationPending,
    bool IsEmailConfirmed);

public sealed record CreatedUserResponse(UserResponse User, bool InvitationSent);

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

/// <summary>A ProblemDetails body. Only <c>status</c> is always sent; the rest depends on the kind of error.</summary>
public sealed record ProblemResponse(
    int Status,
    string? Title = null,
    string? Detail = null,
    string? Code = null,
    Dictionary<string, string[]>? Errors = null);
