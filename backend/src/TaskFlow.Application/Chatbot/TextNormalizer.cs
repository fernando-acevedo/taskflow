using System.Globalization;
using System.Text;

namespace TaskFlow.Application.Chatbot;

/// <summary>
/// Turns raw user text into the form the classifier sees, for both training and prediction: lowercase, no diacritics,
/// no punctuation, single spaces, synonym variants replaced by their canonical token.
/// </summary>
public sealed class TextNormalizer
{
    /// <summary>Draft synonym map used by tests; the real list comes from the knowledge base.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> DefaultSynonyms { get; } =
        new Dictionary<string, IReadOnlyList<string>>
        {
            ["password"] = ["contraseña", "clave", "pass", "pwd", "passwd"],
            ["login"] = ["log in", "sign in", "signin", "iniciar sesion", "ingresar", "entrar"],
            ["email"] = ["correo", "mail", "e-mail", "correo electronico"],
            ["link"] = ["enlace", "vinculo"],
            ["reset"] = ["restablecer", "recuperar", "resetear"],
            ["locked"] = ["bloqueada", "bloqueado", "locked out"],
        };

    // Variants as token arrays, longest (most tokens) first, so "correo electronico" wins over "correo".
    private readonly (string[] Tokens, string Canonical)[] _variants;

    public TextNormalizer(IReadOnlyDictionary<string, IReadOnlyList<string>> synonyms)
    {
        ArgumentNullException.ThrowIfNull(synonyms);
        _variants = synonyms
            .SelectMany(entry => entry.Value.Select(variant => (Tokens: Tokenize(variant), Canonical: Clean(entry.Key))))
            .Where(v => v.Tokens.Length > 0 && v.Canonical.Length > 0)
            .OrderByDescending(v => v.Tokens.Length)
            .ThenByDescending(v => v.Tokens.Sum(t => t.Length))
            .ToArray();
    }

    public string Normalize(string? text)
    {
        var tokens = Tokenize(text);
        if (tokens.Length == 0)
        {
            return "";
        }

        var result = new List<string>(tokens.Length);
        var i = 0;
        while (i < tokens.Length)
        {
            var matched = false;
            foreach (var (variant, canonical) in _variants)
            {
                if (i + variant.Length <= tokens.Length && tokens.AsSpan(i, variant.Length).SequenceEqual(variant))
                {
                    result.Add(canonical);
                    i += variant.Length;
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                result.Add(tokens[i++]);
            }
        }

        return string.Join(' ', result);
    }

    private static string Clean(string text) => string.Join(' ', Tokenize(text));

    private static string[] Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }

        return sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}
