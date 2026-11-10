using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace TaskFlow.Api.Realtime;

/// <summary>
/// The hub connections of this instance, by person (ADR 0023): who they are, in which tenant and which project groups they joined, so
/// that losing access can take them out of the groups or close them. Per instance: with Azure SignalR Service another instance
/// holds other connections, and for those the token's short lifetime is the limit (<c>CloseOnAuthenticationExpiration</c>).
/// </summary>
internal sealed class HubConnections
{
    private readonly ConcurrentDictionary<string, Entry> _connections = new(StringComparer.Ordinal);

    public void Add(string connectionId, Guid userId, Guid? tenantId, HubCallerContext context) =>
        _connections[connectionId] = new Entry(connectionId, userId, tenantId, context);

    public void Remove(string connectionId) => _connections.TryRemove(connectionId, out _);

    public void Joined(string connectionId, Guid projectId)
    {
        if (_connections.TryGetValue(connectionId, out var entry))
        {
            lock (entry.Projects)
            {
                entry.Projects.Add(projectId);
            }
        }
    }

    public void Left(string connectionId, Guid projectId)
    {
        if (_connections.TryGetValue(connectionId, out var entry))
        {
            lock (entry.Projects)
            {
                entry.Projects.Remove(projectId);
            }
        }
    }

    /// <summary>The connections of the person, in the tenant and in the project's group when those are given.</summary>
    public IReadOnlyList<Entry> Of(Guid userId, Guid? tenantId = null, Guid? projectId = null) =>
        [.. _connections.Values.Where(entry =>
            entry.UserId == userId
            && (tenantId is null || entry.TenantId == tenantId)
            && (projectId is null || entry.IsIn(projectId.Value)))];

    internal sealed class Entry(string connectionId, Guid userId, Guid? tenantId, HubCallerContext context)
    {
        public string ConnectionId { get; } = connectionId;

        public Guid UserId { get; } = userId;

        public Guid? TenantId { get; } = tenantId;

        public HubCallerContext Context { get; } = context;

        internal HashSet<Guid> Projects { get; } = [];

        public bool IsIn(Guid projectId)
        {
            lock (Projects)
            {
                return Projects.Contains(projectId);
            }
        }
    }
}
