using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using TaskFlow.Application.Common;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Activity;

namespace TaskFlow.Application.Activity;

/// <param name="Id">The entry's id.</param>
/// <param name="TaskId">Null for entries about members and labels; may name a task that has since been deleted.</param>
/// <param name="ActorId">Who made the change.</param>
/// <param name="ActorName">The actor's current display name.</param>
/// <param name="Type">What happened.</param>
/// <param name="OccurredAt">When it happened.</param>
/// <param name="Changes">A JSON object whose keys depend on <paramref name="Type"/>; see ADR 0009.</param>
/// <param name="ViaApiKey">True when the change was made with an API key (the key's id is not exposed).</param>
public sealed record ActivityEntryDto(
    Guid Id,
    Guid? TaskId,
    Guid ActorId,
    string ActorName,
    ActivityType Type,
    DateTimeOffset OccurredAt,
    JsonElement Changes,
    bool ViaApiKey);

internal static class ActivityMapping
{
    // Entries store user ids; the names are read now so that a renamed user shows under the current name.
    private static readonly string[] UserIdKeys = ["memberId", "assigneeId", "previousAssigneeId"];

    public static async Task<CursorPage<ActivityEntryDto>> ToPageAsync(
        this IReadOnlyList<ActivityEntry> entries,
        int pageSize,
        IIdentityService identity,
        CancellationToken cancellationToken)
    {
        var hasMore = entries.Count > pageSize;
        var page = hasMore ? entries.Take(pageSize).ToList() : entries.ToList();
        var parsed = page.Select(entry => (Entry: entry, Changes: Parse(entry.Changes))).ToList();
        var userIds = parsed
            .SelectMany(row => UserIdKeys.Select(key => UserId(row.Changes, key)))
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Concat(page.Select(entry => entry.ActorId))
            .Distinct()
            .ToList();
        var names = (await identity.FindByIdsAsync(userIds, cancellationToken)).ToDictionary(user => user.Id, user => user.DisplayName);

        var items = parsed.Select(row =>
        {
            foreach (var key in UserIdKeys)
            {
                if (UserId(row.Changes, key) is { } userId)
                {
                    row.Changes[key[..^2] + "Name"] = names.GetValueOrDefault(userId, string.Empty);
                }
            }

            return new ActivityEntryDto(
                row.Entry.Id,
                row.Entry.TaskItemId,
                row.Entry.ActorId,
                names.GetValueOrDefault(row.Entry.ActorId, string.Empty),
                row.Entry.Type,
                row.Entry.OccurredAt,
                JsonSerializer.SerializeToElement(row.Changes),
                row.Entry.ApiKeyId is not null);
        }).ToList();

        var last = page.Count > 0 ? page[^1] : null;
        return new CursorPage<ActivityEntryDto>(
            items,
            hasMore && last is not null ? PageCursor.Encode(last.OccurredAt, last.Sequence.ToString(CultureInfo.InvariantCulture)) : null);
    }

    private static JsonObject Parse(string json) => JsonNode.Parse(json) as JsonObject ?? [];

    private static Guid? UserId(JsonObject changes, string key) =>
        changes[key] is JsonValue value && value.TryGetValue<string>(out var text) && Guid.TryParse(text, out var id) ? id : null;
}
