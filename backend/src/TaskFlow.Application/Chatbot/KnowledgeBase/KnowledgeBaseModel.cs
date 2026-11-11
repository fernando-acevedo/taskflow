namespace TaskFlow.Application.Chatbot.KnowledgeBase;

/// <summary>How a conversation ended, recorded on the terminal step that reaches it.</summary>
public enum ChatOutcome
{
    Completed,
    Escalated,
}

/// <summary>Global knobs of the chatbot (<c>settings.json</c>).</summary>
public sealed record KnowledgeBaseSettings
{
    public string Version { get; init; } = "";

    public IReadOnlyList<string> Locales { get; init; } = [];

    public int MinUtterancesPerLocale { get; init; }

    public double ConfidenceThreshold { get; init; }

    public double MinMargin { get; init; }

    public int MaxConsecutiveFallbacks { get; init; }

    public int SessionIdleMinutes { get; init; }

    public string StartStep { get; init; } = "";

    public string FallbackStep { get; init; } = "";

    public string EscalateStep { get; init; } = "";

    public string OutOfScopeIntent { get; init; } = "";

    /// <summary>Values for the <c>{name}</c> placeholders of the texts.</summary>
    public IReadOnlyDictionary<string, string> Links { get; init; } = new Dictionary<string, string>();
}

/// <summary>A button under a bot message. Needs <see cref="Next"/> or <see cref="Url"/> (or both).</summary>
public sealed record QuickReply
{
    public string Id { get; init; } = "";

    /// <summary>Label per locale.</summary>
    public IReadOnlyDictionary<string, string> Label { get; init; } = new Dictionary<string, string>();

    public string? Next { get; init; }

    public string? Url { get; init; }

    /// <summary>Jumps to the entry step of this intent.</summary>
    public string? Intent { get; init; }
}

/// <summary>One node of the scripted dialogue.</summary>
public sealed record DialogStep
{
    /// <summary>Message per locale; may contain <c>{support}</c>-style placeholders.</summary>
    public IReadOnlyDictionary<string, string> Text { get; init; } = new Dictionary<string, string>();

    public IReadOnlyList<QuickReply> QuickReplies { get; init; } = [];

    public bool ExpectsText { get; init; }

    public bool End { get; init; }

    public ChatOutcome? Outcome { get; init; }
}

/// <summary>An intent file (<c>intents/&lt;Intent&gt;.json</c>): training data plus its dialogue steps.</summary>
public sealed record IntentDefinition
{
    public string Intent { get; init; } = "";

    public string Description { get; init; } = "";

    public IReadOnlyList<string> Triggers { get; init; } = [];

    public IReadOnlyList<string> NotTriggers { get; init; } = [];

    public IReadOnlyList<string> ConfusedWith { get; init; } = [];

    /// <summary>Training phrases per locale.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Utterances { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();

    public string EntryStep { get; init; } = "";

    public IReadOnlyDictionary<string, DialogStep> Steps { get; init; } = new Dictionary<string, DialogStep>();

    /// <summary>File the intent came from, for error messages; set by the loader.</summary>
    public string Source { get; init; } = "";
}

/// <summary>Everything read from a KB folder, before it is validated.</summary>
public sealed record KnowledgeBaseContent
{
    public required KnowledgeBaseSettings Settings { get; init; }

    /// <summary>Canonical word to its variants.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Synonyms { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>Steps not tied to one intent (<c>common.json</c>).</summary>
    public IReadOnlyDictionary<string, DialogStep> CommonSteps { get; init; } = new Dictionary<string, DialogStep>();

    public IReadOnlyList<IntentDefinition> Intents { get; init; } = [];
}
