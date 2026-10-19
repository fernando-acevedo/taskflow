namespace TaskFlow.Application.Common;

public static class StringExtensions
{
    /// <summary>Trims optional text and treats blank input as absent.</summary>
    public static string? TrimToNull(this string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
