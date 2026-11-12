using System.Text.RegularExpressions;

namespace TaskFlow.Application.Chatbot.KnowledgeBase;

/// <summary>Read access to the validated chatbot knowledge base.</summary>
public interface IKnowledgeBase
{
    KnowledgeBaseSettings Settings { get; }

    IReadOnlyDictionary<string, IReadOnlyList<string>> Synonyms { get; }

    IReadOnlyList<IntentDefinition> Intents { get; }

    /// <summary>All steps (common and per intent) by id.</summary>
    IReadOnlyDictionary<string, DialogStep> Steps { get; }

    DialogStep? FindStep(string stepId);

    /// <summary>The first (reviewed original) text variant of the step in the locale, placeholders replaced.</summary>
    string GetText(string stepId, string locale);

    /// <summary>All text variants of the step in the locale, placeholders replaced; the runtime picks one at random.</summary>
    IReadOnlyList<string> GetTexts(string stepId, string locale);

    /// <summary>A quick reply label in the locale, with placeholders replaced.</summary>
    string GetLabel(QuickReply reply, string locale);
}

/// <summary>An immutable, already validated KB.</summary>
public sealed partial class KnowledgeBase : IKnowledgeBase
{
    private readonly Dictionary<string, DialogStep> _steps = new(StringComparer.Ordinal);

    public KnowledgeBase(KnowledgeBaseContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Settings = content.Settings;
        Synonyms = content.Synonyms;
        Intents = content.Intents;
        foreach (var (id, step) in content.CommonSteps)
        {
            _steps[id] = step;
        }

        foreach (var intent in content.Intents)
        {
            foreach (var (id, step) in intent.Steps)
            {
                _steps[id] = step;
            }
        }
    }

    public KnowledgeBaseSettings Settings { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Synonyms { get; }

    public IReadOnlyList<IntentDefinition> Intents { get; }

    public IReadOnlyDictionary<string, DialogStep> Steps => _steps;

    public DialogStep? FindStep(string stepId) => _steps.GetValueOrDefault(stepId);

    public string GetText(string stepId, string locale) => GetTexts(stepId, locale)[0];

    public IReadOnlyList<string> GetTexts(string stepId, string locale)
    {
        var step = FindStep(stepId) ?? throw new KeyNotFoundException($"Unknown step '{stepId}'.");
        return step.Text[locale].Select(Render).ToList();
    }

    public string GetLabel(QuickReply reply, string locale)
    {
        ArgumentNullException.ThrowIfNull(reply);
        return Render(reply.Label[locale]);
    }

    private string Render(string text) =>
        PlaceholderRegex().Replace(text, m =>
            Settings.Links.TryGetValue(m.Groups[1].Value, out var value) && !string.IsNullOrEmpty(value) ? value : m.Value);

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex PlaceholderRegex();
}
