using System.Text.Json;
using System.Text.Json.Serialization;
using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Activity;

namespace TaskFlow.Application.Activity;

/// <summary>
/// Handlers call <see cref="Record"/> next to the change they make, and their own <c>SaveChangesAsync</c> stores
/// the entry in the same transaction (ADR 0009). It never saves by itself.
/// </summary>
public sealed class ActivityLog(IAppDbContext db, ICurrentUser currentUser, TimeProvider time)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <param name="projectId">The project the entry belongs to.</param>
    /// <param name="type">What happened.</param>
    /// <param name="changes">An object (usually anonymous) with the values the entry's text needs; null properties are left out.</param>
    /// <param name="taskId">The task the entry is about, if any.</param>
    public void Record(Guid projectId, ActivityType type, object changes, Guid? taskId = null)
    {
        var now = time.GetUtcNow();
        db.ActivityEntries.Add(ActivityEntry.Create(
            NextId(now), projectId, taskId, currentUser.Id, type, JsonSerializer.Serialize(changes, Json), now, currentUser.ApiKeyId));
    }

    // EF Core inserts the entries of one SaveChanges ordered by primary key, and the identity column `Sequence`
    // follows that order. Two v7 Guids made in the same millisecond compare randomly, so each id's timestamp
    // advances at least 1 ms past the previous one, which keeps ids in the order they were recorded.
    private Guid NextId(DateTimeOffset now)
    {
        var stamp = _lastStamp is { } last && now <= last ? last.AddMilliseconds(1) : now;
        _lastStamp = stamp;
        return Guid.CreateVersion7(stamp);
    }

    private DateTimeOffset? _lastStamp;
}
