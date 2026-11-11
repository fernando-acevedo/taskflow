using System.Text.RegularExpressions;

namespace TaskFlow.Application.Chatbot;

/// <summary>Masks emails, URLs, long numbers and secrets in raw text. Run it on the raw input before storing or logging.</summary>
public static partial class PiiRedactor
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private const int TimeoutMs = 200;

    // "password is X", "pwd: X", "clave: X", "mi clave es X" (explicit connector), or "my password X" / "mi contraseña X"
    // where X must contain a digit or symbol so "my password does not work" stays intact.
    [GeneratedRegex(
        @"\b(?<prefix>(?:(?:my\s+|your\s+|mi\s+|tu\s+)?(?:password|passwd|pwd|contraseña|contrasena|clave)\s*(?:is|es|:|=)\s*)|(?:(?:my\s+password|mi\s+contraseña|mi\s+contrasena|mi\s+clave)\s+(?=\S*[^\p{L}\s])))(?<secret>\S+)",
        Options, TimeoutMs)]
    private static partial Regex SecretRegex();

    [GeneratedRegex(@"[\p{L}\p{N}._%+\-]+@[\p{L}\p{N}.\-]+\.[\p{L}]{2,}", Options, TimeoutMs)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"\b(?:https?://|www\.)\S+", Options, TimeoutMs)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"\d{6,}", Options, TimeoutMs)]
    private static partial Regex NumberRegex();

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        try
        {
            var result = SecretRegex().Replace(text, m => m.Groups["prefix"].Value + "[secret]");
            result = EmailRegex().Replace(result, "[email]");
            result = UrlRegex().Replace(result, "[url]");
            return NumberRegex().Replace(result, "[number]");
        }
        catch (RegexMatchTimeoutException)
        {
            return "[redacted]";
        }
    }
}
