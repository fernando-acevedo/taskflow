namespace TaskFlow.Application.Chatbot;

/// <summary>Rough language detection for logging only: "en", "es" or "unknown".</summary>
public static class LanguageGuesser
{
    public const string English = "en";
    public const string Spanish = "es";
    public const string Unknown = "unknown";

    private static readonly HashSet<string> EnglishWords = new(StringComparer.Ordinal)
    {
        "the", "is", "are", "and", "to", "of", "in", "my", "i", "how", "do", "can", "not", "with", "for", "this",
        "that", "what", "you", "it", "me", "have", "cannot", "cant", "dont", "forgot", "where", "why",
    };

    private static readonly HashSet<string> SpanishWords = new(StringComparer.Ordinal)
    {
        "el", "la", "los", "las", "es", "son", "y", "de", "en", "mi", "yo", "como", "puedo", "no", "con", "para",
        "esto", "que", "se", "un", "una", "olvide", "donde", "por", "quiero", "tengo", "mis", "mas",
    };

    private const string SpanishChars = "ñ¿¡áéíóúü";

    public static string Guess(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Unknown;
        }

        var lower = text.ToLowerInvariant();
        var en = 0;
        var es = 0;
        foreach (var c in lower)
        {
            if (SpanishChars.Contains(c))
            {
                es += 2;
            }
        }

        var words = lower.Split(
            [' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?', '¿', '¡', '"', '(', ')'],
            StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            var w = word.Trim('\'');
            if (EnglishWords.Contains(w))
            {
                en++;
            }

            if (SpanishWords.Contains(w))
            {
                es++;
            }
        }

        if (en == es)
        {
            return Unknown;
        }

        return en > es ? English : Spanish;
    }
}
