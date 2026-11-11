namespace TaskFlow.Domain.Chatbot;

public enum IntentSource
{
    Text,
    QuickReply,
}

public enum ChatOutcome
{
    Answered,
    Fallback,
    Escalated,
}

/// <summary>What the classifier made of one user input. Kept apart from the messages for analytics and retraining.</summary>
public sealed class IntentLog
{
    public const int TextMaxLength = 1000;
    public const int NameMaxLength = 100;
    public const int LanguageMaxLength = 10;

    public IntentLog(
        Guid sessionId,
        Guid? messageId,
        IntentSource source,
        string redactedText,
        string normalizedText,
        string? predictedIntent,
        double? confidence,
        string? secondIntent,
        double? secondConfidence,
        string receivedLocale,
        string detectedLanguage,
        string? modelVersion,
        ChatOutcome outcome,
        DateTimeOffset createdAt)
    {
        Id = Guid.CreateVersion7(createdAt);
        SessionId = sessionId;
        MessageId = messageId;
        Source = source;
        RedactedText = redactedText;
        NormalizedText = normalizedText;
        PredictedIntent = predictedIntent;
        Confidence = confidence;
        SecondIntent = secondIntent;
        SecondConfidence = secondConfidence;
        ReceivedLocale = receivedLocale;
        DetectedLanguage = detectedLanguage;
        ModelVersion = modelVersion;
        Outcome = outcome;
        CreatedAt = createdAt;
    }

    private IntentLog()
    {
        RedactedText = string.Empty;
        NormalizedText = string.Empty;
        ReceivedLocale = string.Empty;
        DetectedLanguage = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid SessionId { get; private set; }

    public Guid? MessageId { get; private set; }

    public IntentSource Source { get; private set; }

    public string RedactedText { get; private set; }

    public string NormalizedText { get; private set; }

    public string? PredictedIntent { get; private set; }

    public double? Confidence { get; private set; }

    public string? SecondIntent { get; private set; }

    public double? SecondConfidence { get; private set; }

    public string ReceivedLocale { get; private set; }

    /// <summary>en, es or unknown.</summary>
    public string DetectedLanguage { get; private set; }

    public string? ModelVersion { get; private set; }

    public ChatOutcome Outcome { get; private set; }

    /// <summary>Whether the user took the answer as useful; null until they say so.</summary>
    public bool? Accepted { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void MarkAccepted(bool accepted) => Accepted = accepted;
}
