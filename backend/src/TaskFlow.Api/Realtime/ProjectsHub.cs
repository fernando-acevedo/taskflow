using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using TaskFlow.Application.Projects;
using TaskFlow.Domain.Tenancy;
using TaskFlow.Infrastructure.ApiKeys;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Api.Realtime;

/// <summary>
/// Real-time updates of the projects a person has open (ADR 0023). Authenticated with the bearer token (query <c>access_token</c>) and
/// scoped to a tenant (query <c>tenant</c>); the middleware does both before the hub runs, exactly as for a request to the API, and
/// this class reads the result. Only people connect: an API key is turned away.
/// </summary>
/// <remarks>
/// Rate limiting: the global limiter charges the HTTP requests (negotiate, the WebSocket upgrade, every long-polling request), but a
/// message on an open WebSocket is not an HTTP request, so a busy connection is never cut off by the token bucket.
/// </remarks>
[Authorize]
internal sealed class ProjectsHub(ProjectAccess access, TenantContext tenant, HubCaller caller, HubConnections connections) : Hub
{
    public const string Path = "/hubs/projects";

    /// <summary>The connection's tenant, kept by the tenant middleware in <c>HttpContext.Items</c>.</summary>
    internal const string TenantItem = "TaskFlow.HubTenant";

    private const string TenantKey = "tenant";

    public static string Group(Guid tenantId, Guid projectId) => $"project:{tenantId}:{projectId}";

    public override Task OnConnectedAsync()
    {
        if (Context.User?.HasClaim(claim => claim.Type == ApiKeyDefaults.KeyIdClaim) == true)
        {
            Context.Abort();
            return Task.CompletedTask;
        }

        if (Context.GetHttpContext()?.Items[TenantItem] is HubTenant resolved)
        {
            Context.Items[TenantKey] = resolved;
        }

        if (Guid.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            connections.Add(Context.ConnectionId, userId, (Context.Items[TenantKey] as HubTenant)?.TenantId, Context);
        }

        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>Joins the project's group when the person may view it; otherwise fails with the API's error code (404 or 403).</summary>
    public async Task JoinProject(Guid projectId)
    {
        var tenantId = UseCaller();
        var allowed = await access.GetAsync(projectId, ProjectAccessLevel.View, Context.ConnectionAborted);
        if (!allowed.IsSuccess)
        {
            throw new HubException(allowed.Error.Code);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, Group(tenantId, projectId), Context.ConnectionAborted);
        connections.Joined(Context.ConnectionId, projectId);
    }

    public Task LeaveProject(Guid projectId)
    {
        var tenantId = UseCaller();
        connections.Left(Context.ConnectionId, projectId);
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(tenantId, projectId), Context.ConnectionAborted);
    }

    /// <summary>A hub call runs in its own scope, so the caller and the tenant of the connection are set again for it.</summary>
    private Guid UseCaller()
    {
        if (Context.Items[TenantKey] is not HubTenant resolved || Context.User is not ClaimsPrincipal user)
        {
            throw new HubException(TenantErrors.NotFound.Code);
        }

        caller.User = user;
        tenant.Use(resolved.TenantId, resolved.Role);
        return resolved.TenantId;
    }
}

internal sealed record HubTenant(Guid TenantId, TenantRole? Role);

/// <summary>The person behind a hub call, for the code that normally reads the HTTP request's user.</summary>
internal sealed class HubCaller
{
    public ClaimsPrincipal? User { get; set; }
}
