using Microsoft.AspNetCore.SignalR;
using TaskFlow.Application.Abstractions;

namespace TaskFlow.Api.Realtime;

/// <summary>Takes the connections of a person out of the project groups, or closes them, when they lose access (ADR 0023).</summary>
internal sealed class RealtimeRevoker(IHubContext<ProjectsHub> hub, HubConnections connections) : IRealtimeRevoker
{
    public async Task ProjectAccessRevokedAsync(Guid projectId, Guid userId, CancellationToken cancellationToken)
    {
        foreach (var entry in connections.Of(userId, projectId: projectId))
        {
            connections.Left(entry.ConnectionId, projectId);
            if (entry.TenantId is { } tenantId)
            {
                await hub.Groups.RemoveFromGroupAsync(entry.ConnectionId, ProjectsHub.Group(tenantId, projectId), cancellationToken);
            }
        }
    }

    // The client reconnects by itself and joins again: the hub checks the access anew (and a person with none is turned away).
    public Task TenantAccessChangedAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
        AbortAsync(connections.Of(userId, tenantId));

    public Task UserDeactivatedAsync(Guid userId, CancellationToken cancellationToken) =>
        AbortAsync(connections.Of(userId));

    private static Task AbortAsync(IReadOnlyList<HubConnections.Entry> entries)
    {
        foreach (var entry in entries)
        {
            entry.Context.Abort();
        }

        return Task.CompletedTask;
    }
}
