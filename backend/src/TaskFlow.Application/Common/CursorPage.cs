using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace TaskFlow.Application.Common;

/// <summary>A keyset page: the items and an opaque cursor for the next one, null at the end (ADR 0025).</summary>
public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>The opaque cursor: base64url of a timestamp (UTC ticks) and a tiebreaker (sequence or id) of the last item.</summary>
public static class PageCursor
{
    /// <summary>The longest cursor accepted (a 19-digit tick count, a separator and a GUID are 75 characters): anything longer is not one of ours.</summary>
    public const int MaxLength = 100;

    public static string Encode(DateTimeOffset at, string tiebreaker) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes($"{at.UtcTicks.ToString(CultureInfo.InvariantCulture)}|{tiebreaker}"));

    public static bool TryDecode(string? cursor, out DateTimeOffset at, out string tiebreaker)
    {
        at = default;
        tiebreaker = string.Empty;
        if (string.IsNullOrEmpty(cursor) || cursor.Length > MaxLength || !Base64Url.IsValid(cursor))
        {
            return false;
        }

        var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor)).Split('|');
        if (parts.Length != 2
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks > DateTimeOffset.MaxValue.UtcTicks)
        {
            return false;
        }

        at = new DateTimeOffset(ticks, TimeSpan.Zero);
        tiebreaker = parts[1];
        return true;
    }

    public static bool IsValidSequence(string? cursor) =>
        cursor is null || (TryDecode(cursor, out _, out var key) && long.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out _));

    public static bool IsValidId(string? cursor) =>
        cursor is null || (TryDecode(cursor, out _, out var key) && Guid.TryParse(key, out _));
}
