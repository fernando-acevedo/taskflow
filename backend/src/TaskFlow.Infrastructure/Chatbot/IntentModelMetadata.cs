namespace TaskFlow.Infrastructure.Chatbot;

/// <summary>Describes what a model was trained on, stored inside the model zip.</summary>
public sealed record IntentModelMetadata
{
    public string KbVersion { get; init; } = "";

    /// <summary>SHA-256 (hex) of the sorted (intent, locale, utterance) training set.</summary>
    public string TrainingSetHash { get; init; } = "";

    /// <summary>SHA-256 (hex) of the synonym map, which changes the normalized training text.</summary>
    public string SynonymsHash { get; init; } = "";

    public IReadOnlyList<string> Intents { get; init; } = [];

    public int UtteranceCount { get; init; }

    public string ModelVersion => $"{KbVersion}+{TrainingSetHash[..Math.Min(8, TrainingSetHash.Length)]}";
}
