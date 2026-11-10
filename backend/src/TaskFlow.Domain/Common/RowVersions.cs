namespace TaskFlow.Domain.Common;

/// <summary>Compares the row version a client sent with the current one (ADR 0022).</summary>
public static class RowVersions
{
    /// <summary>The version of <c>If-Match: *</c>: matches any existing item without comparing.</summary>
    public static readonly byte[] Any = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];

    /// <summary>
    /// True when <paramref name="requested"/> is <see cref="Any"/>, equals <paramref name="current"/>, or is several
    /// versions side by side (an <c>If-Match</c> list) and one of them equals <paramref name="current"/>.
    /// </summary>
    public static bool Matches(byte[] current, byte[] requested)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(requested);

        if (ReferenceEquals(requested, Any) || current.AsSpan().SequenceEqual(requested))
        {
            return true;
        }

        if (current.Length == 0 || requested.Length <= current.Length || requested.Length % current.Length != 0)
        {
            return false;
        }

        for (var offset = 0; offset < requested.Length; offset += current.Length)
        {
            if (requested.AsSpan(offset, current.Length).SequenceEqual(current))
            {
                return true;
            }
        }

        return false;
    }
}
