using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TaskFlow.Application.Chatbot.KnowledgeBase;

/// <summary>Options for <see cref="KnowledgeBaseValidator"/>.</summary>
/// <param name="MinUtterancesPerLocale">Replaces <c>settings.minUtterancesPerLocale</c> (fixtures relax it).</param>
public sealed record KnowledgeBaseValidationOptions(int? MinUtterancesPerLocale = null);

/// <summary>
/// Checks a knowledge base and returns every problem found (not only the first), each with file and step context.
/// Reachability starts from the start, fallback and escalate steps and from every intent's entry step.
/// </summary>
public static partial class KnowledgeBaseValidator
{
    private const string CommonFile = "common.json";
    private const string SettingsFile = "settings.json";

    // Voseo / regionalisms the neutral Spanish must not use. Words that are valid neutral Spanish once the accent
    // is removed (revisa, sabes, hace...) only match with the accent; the others match either way.
    private static readonly (string Word, bool IgnoreAccents)[] BlockedSpanish =
    [
        ("podés", true), ("tenés", true), ("querés", true), ("sabés", false), ("hacé", false),
        ("iniciá", false), ("ingresá", false), ("revisá", false), ("probá", false), ("escribí", false),
        ("elegí", false), ("volvé", false), ("fijate", true), ("decime", true), ("vos", true),
        ("che", true), ("acá", true),
    ];

    public static IReadOnlyList<string> Validate(KnowledgeBaseContent kb, KnowledgeBaseValidationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(kb);
        var problems = new List<string>();
        var settings = kb.Settings;
        var minUtterances = options?.MinUtterancesPerLocale ?? settings.MinUtterancesPerLocale;

        ValidateSettings(settings, problems);

        // Index of steps by id, with their file; reports ids defined twice.
        var steps = new Dictionary<string, (DialogStep Step, string File)>(StringComparer.Ordinal);
        void Index(IReadOnlyDictionary<string, DialogStep> source, string file)
        {
            foreach (var (id, step) in source)
            {
                if (steps.TryGetValue(id, out var existing))
                {
                    problems.Add($"{file}: step '{id}' is already defined in {existing.File}.");
                }
                else
                {
                    steps[id] = (step, file);
                }
            }
        }

        Index(kb.CommonSteps, CommonFile);
        var intentNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var intent in kb.Intents)
        {
            Index(intent.Steps, FileOf(intent));
            if (!intentNames.Add(intent.Intent))
            {
                problems.Add($"{FileOf(intent)}: intent '{intent.Intent}' is defined more than once.");
            }
        }

        foreach (var (name, id) in new[]
                 {
                     ("startStep", settings.StartStep), ("fallbackStep", settings.FallbackStep),
                     ("escalateStep", settings.EscalateStep),
                 })
        {
            if (!steps.ContainsKey(id))
            {
                problems.Add($"{SettingsFile}: {name} '{id}' does not exist.");
            }
        }

        if (!intentNames.Contains(settings.OutOfScopeIntent))
        {
            problems.Add($"{SettingsFile}: outOfScopeIntent '{settings.OutOfScopeIntent}' does not exist.");
        }

        foreach (var (id, (step, file)) in steps)
        {
            ValidateStep(id, step, file, kb, steps, intentNames, problems);
        }

        foreach (var intent in kb.Intents)
        {
            ValidateIntent(intent, kb, steps, intentNames, minUtterances, problems);
        }

        ValidateUtteranceDuplicates(kb.Intents, problems);
        ValidateReachability(kb, steps, problems);
        return problems;
    }

    private static string FileOf(IntentDefinition intent) =>
        string.IsNullOrEmpty(intent.Source) ? $"intents/{intent.Intent}.json" : intent.Source;

    private static void ValidateSettings(KnowledgeBaseSettings s, List<string> problems)
    {
        if (s.Locales.Count == 0)
        {
            problems.Add($"{SettingsFile}: locales must not be empty.");
        }

        if (s.Locales.Distinct(StringComparer.Ordinal).Count() != s.Locales.Count)
        {
            problems.Add($"{SettingsFile}: locales has duplicates.");
        }

        if (s.ConfidenceThreshold is <= 0 or > 1)
        {
            problems.Add($"{SettingsFile}: confidenceThreshold must be in (0, 1].");
        }

        if (s.MinMargin is < 0 or > 1)
        {
            problems.Add($"{SettingsFile}: minMargin must be in [0, 1].");
        }

        if (s.MaxConsecutiveFallbacks < 1)
        {
            problems.Add($"{SettingsFile}: maxConsecutiveFallbacks must be at least 1.");
        }

        if (s.SessionIdleMinutes < 1)
        {
            problems.Add($"{SettingsFile}: sessionIdleMinutes must be at least 1.");
        }

        if (s.MinUtterancesPerLocale < 0)
        {
            problems.Add($"{SettingsFile}: minUtterancesPerLocale must not be negative.");
        }
    }

    private static void ValidateStep(
        string id,
        DialogStep step,
        string file,
        KnowledgeBaseContent kb,
        Dictionary<string, (DialogStep Step, string File)> steps,
        HashSet<string> intentNames,
        List<string> problems)
    {
        var where = $"{file}, step '{id}'";
        CheckLocalized(step.Text, $"{where}, text", kb, problems);

        if (!step.End && step.QuickReplies.Count == 0 && !step.ExpectsText)
        {
            problems.Add($"{where}: a step that is not an end needs quick replies or expectsText.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reply in step.QuickReplies)
        {
            var rwhere = $"{where}, quick reply '{reply.Id}'";
            if (string.IsNullOrWhiteSpace(reply.Id))
            {
                problems.Add($"{where}: a quick reply has no id.");
            }
            else if (!ids.Add(reply.Id))
            {
                problems.Add($"{rwhere}: id is repeated in the step.");
            }

            CheckLocalized(reply.Label, $"{rwhere}, label", kb, problems);

            if (string.IsNullOrWhiteSpace(reply.Next) && string.IsNullOrWhiteSpace(reply.Url))
            {
                problems.Add($"{rwhere}: needs next or url.");
            }

            if (!string.IsNullOrWhiteSpace(reply.Next) && !steps.ContainsKey(reply.Next))
            {
                problems.Add($"{rwhere}: next '{reply.Next}' does not exist.");
            }

            if (!string.IsNullOrWhiteSpace(reply.Intent) && !intentNames.Contains(reply.Intent))
            {
                problems.Add($"{rwhere}: intent '{reply.Intent}' does not exist.");
            }
        }
    }

    // Every locale present and non-empty, no extra locales, same placeholders everywhere, placeholders known,
    // and no voseo in Spanish.
    private static void CheckLocalized(
        IReadOnlyDictionary<string, string> values,
        string where,
        KnowledgeBaseContent kb,
        List<string> problems)
    {
        var locales = kb.Settings.Locales;
        foreach (var locale in locales)
        {
            if (!values.TryGetValue(locale, out var v) || string.IsNullOrWhiteSpace(v))
            {
                problems.Add($"{where}: missing or empty '{locale}' value.");
            }
        }

        foreach (var locale in values.Keys.Where(k => !locales.Contains(k)))
        {
            problems.Add($"{where}: locale '{locale}' is not in settings.locales.");
        }

        var present = values.Where(kv => locales.Contains(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value)).ToList();
        var sets = present.ToDictionary(kv => kv.Key, kv => Placeholders(kv.Value));
        foreach (var (locale, set) in sets)
        {
            foreach (var name in set.Where(n => !kb.Settings.Links.ContainsKey(n)))
            {
                problems.Add($"{where} ({locale}): placeholder {{{name}}} is not in settings.links.");
            }
        }

        if (sets.Count > 1)
        {
            var first = sets.First();
            foreach (var other in sets.Skip(1).Where(o => !o.Value.SetEquals(first.Value)))
            {
                problems.Add($"{where}: placeholders differ between '{first.Key}' ({Format(first.Value)}) and '{other.Key}' ({Format(other.Value)}).");
            }
        }

        if (values.TryGetValue("es", out var es) && !string.IsNullOrWhiteSpace(es))
        {
            foreach (var word in BlockedWords(es))
            {
                problems.Add($"{where} (es): '{word}' is voseo/regionalism; use neutral Spanish.");
            }
        }
    }

    private static string Format(HashSet<string> set) =>
        set.Count == 0 ? "none" : string.Join(", ", set.Order(StringComparer.Ordinal).Select(n => $"{{{n}}}"));

    private static HashSet<string> Placeholders(string text) =>
        PlaceholderRegex().Matches(text).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> BlockedWords(string text)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in WordRegex().Matches(text).Cast<Match>())
        {
            var word = m.Value.ToLowerInvariant();
            var plain = RemoveAccents(word);
            foreach (var (blocked, ignoreAccents) in BlockedSpanish)
            {
                if (word == blocked || (ignoreAccents && plain == RemoveAccents(blocked)))
                {
                    found.Add(m.Value);
                }
            }
        }

        return found;
    }

    private static string RemoveAccents(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static void ValidateIntent(
        IntentDefinition intent,
        KnowledgeBaseContent kb,
        Dictionary<string, (DialogStep Step, string File)> steps,
        HashSet<string> intentNames,
        int minUtterances,
        List<string> problems)
    {
        var file = FileOf(intent);
        var locales = kb.Settings.Locales;
        if (string.IsNullOrWhiteSpace(intent.Intent))
        {
            problems.Add($"{file}: intent name is empty.");
        }

        if (!steps.ContainsKey(intent.EntryStep))
        {
            problems.Add($"{file}: entryStep '{intent.EntryStep}' does not exist.");
        }

        foreach (var other in intent.ConfusedWith.Where(c => !intentNames.Contains(c)))
        {
            problems.Add($"{file}: confusedWith '{other}' does not exist.");
        }

        foreach (var locale in locales)
        {
            var count = intent.Utterances.TryGetValue(locale, out var list)
                ? list.Count(u => !string.IsNullOrWhiteSpace(u))
                : 0;
            if (count < minUtterances)
            {
                problems.Add($"{file}: '{locale}' has {count} utterances, at least {minUtterances} required.");
            }
        }

        foreach (var locale in intent.Utterances.Keys.Where(k => !locales.Contains(k)))
        {
            problems.Add($"{file}: utterances has locale '{locale}', which is not in settings.locales.");
        }

        foreach (var (locale, list) in intent.Utterances)
        {
            if (list.Any(string.IsNullOrWhiteSpace))
            {
                problems.Add($"{file}: '{locale}' has an empty utterance.");
            }
        }
    }

    // Repeated inside an intent (per locale) or shared between intents (any locale).
    private static void ValidateUtteranceDuplicates(IReadOnlyList<IntentDefinition> intents, List<string> problems)
    {
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var intent in intents)
        {
            var file = FileOf(intent);
            var ownKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (locale, list) in intent.Utterances)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var key in list.Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim().ToLowerInvariant()))
                {
                    if (!seen.Add(key))
                    {
                        problems.Add($"{file}: utterance '{key}' is repeated in '{locale}'.");
                    }

                    ownKeys.Add(key);
                }
            }

            foreach (var key in ownKeys)
            {
                if (owners.TryGetValue(key, out var other))
                {
                    problems.Add($"{file}: utterance '{key}' is also in {other}.");
                }
                else
                {
                    owners[key] = file;
                }
            }
        }
    }

    private static void ValidateReachability(
        KnowledgeBaseContent kb,
        Dictionary<string, (DialogStep Step, string File)> steps,
        List<string> problems)
    {
        var entryByIntent = kb.Intents.GroupBy(i => i.Intent).ToDictionary(g => g.Key, g => g.First().EntryStep);
        var pending = new Stack<string>();
        var reached = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string? id)
        {
            if (!string.IsNullOrWhiteSpace(id) && steps.ContainsKey(id) && reached.Add(id))
            {
                pending.Push(id);
            }
        }

        Visit(kb.Settings.StartStep);
        Visit(kb.Settings.FallbackStep);
        Visit(kb.Settings.EscalateStep);
        foreach (var intent in kb.Intents)
        {
            Visit(intent.EntryStep);
        }

        while (pending.TryPop(out var id))
        {
            foreach (var reply in steps[id].Step.QuickReplies)
            {
                Visit(reply.Next);
                if (reply.Intent is not null && entryByIntent.TryGetValue(reply.Intent, out var entry))
                {
                    Visit(entry);
                }
            }
        }

        foreach (var (id, (_, file)) in steps.Where(s => !reached.Contains(s.Key)))
        {
            problems.Add($"{file}, step '{id}': not reachable from the start step or any intent entryStep.");
        }
    }

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex WordRegex();
}
