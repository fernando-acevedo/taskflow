namespace TaskFlow.Application.Chatbot;

/// <summary>One intent label with its probability.</summary>
public sealed record IntentScore(string Intent, double Score);

/// <summary>The classifier output: every intent ranked by score, best first.</summary>
public sealed record IntentPrediction(IReadOnlyList<IntentScore> Ranked, string ModelVersion)
{
    public IntentScore Top => Ranked[0];

    public IntentScore? Second => Ranked.Count > 1 ? Ranked[1] : null;
}

/// <summary>Maps normalized user text (see <see cref="TextNormalizer"/>) to an intent. Thread-safe.</summary>
public interface IIntentClassifier
{
    /// <summary>False when no model could be loaded; <see cref="Classify"/> then throws.</summary>
    bool IsLoaded { get; }

    string ModelVersion { get; }

    IntentPrediction Classify(string normalizedText);
}
