namespace TaskFlow.Application.Abstractions;

/// <summary>
/// Cuts what a person receives in real time when they lose access (ADR 0023): the use cases call it after the commit. Best effort,
/// like the rest of the real-time layer; implemented by the API, which holds the connections.
/// </summary>
public interface IRealtimeRevoker
{
    /// <summary>The person is no longer a member of the project: their connections leave its group.</summary>
    Task ProjectAccessRevokedAsync(Guid projectId, Guid userId, CancellationToken cancellationToken);

    /// <summary>The person's role in the tenant changed or they left it: their connections in it are closed and must join again (access is checked anew).</summary>
    Task TenantAccessChangedAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);

    /// <summary>The person was deactivated: every connection they have is closed.</summary>
    Task UserDeactivatedAsync(Guid userId, CancellationToken cancellationToken);
}
