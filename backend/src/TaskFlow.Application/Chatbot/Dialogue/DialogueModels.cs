using TaskFlow.Application.Chatbot.KnowledgeBase;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Chatbot.Dialogue;

/// <summary>
/// Where a conversation is. Maps to <c>ChatSession</c> (Locale, CurrentIntent, CurrentStep, ConsecutiveFallbacks);
/// the engine itself never touches the entity. Locale is fixed for the whole session.
/// </summary>
public sealed record DialogueState(
    string Locale,
    string? Intent,
    string? Step,
    int ConsecutiveFallbacks,
    bool Ended = false,
    ChatOutcome? Outcome = null);

/// <summary>What the user did: clicked a quick reply, or typed text (with the classifier's verdict on it).</summary>
public abstract record DialogueInput
{
    public static DialogueInput QuickReply(string id) => new QuickReplyInput(id);

    /// <summary>Free text; <paramref name="prediction"/> is null when no classifier is available (counts as a miss).</summary>
    public static DialogueInput Text(IntentPrediction? prediction) => new TextInput(prediction);
}

public sealed record QuickReplyInput(string Id) : DialogueInput;

public sealed record TextInput(IntentPrediction? Prediction) : DialogueInput;

/// <summary>A button shown to the user, label already in the session locale.</summary>
public sealed record ReplyOption(string Id, string Label, string? Url);

/// <summary>The bot's answer for one turn.</summary>
/// <param name="Text">The step text (one variant picked at random, placeholders filled).</param>
/// <param name="QuickReplies">Visible buttons (a button whose url is an unset link is hidden).</param>
/// <param name="Url">Set when the user's quick reply carried a url for the client to open.</param>
/// <param name="StepId">The step shown.</param>
/// <param name="Ended">The conversation is over; <paramref name="Outcome"/> says how.</param>
/// <param name="Outcome">Completed or Escalated when ended, else null.</param>
/// <param name="IsFallback">The text was not understood (fallback step shown, or escalation after repeated misses).</param>
/// <param name="MatchedIntent">The intent the free text was routed to, when it was accepted.</param>
public sealed record DialogueReply(
    string Text,
    IReadOnlyList<ReplyOption> QuickReplies,
    string? Url,
    string StepId,
    bool Ended,
    ChatOutcome? Outcome,
    bool IsFallback,
    string? MatchedIntent);

/// <summary>The reply plus the state to persist.</summary>
public sealed record DialogueTurn(DialogueState State, DialogueReply Reply);

public static class DialogueErrors
{
    public static readonly Error UnknownQuickReply =
        Error.Invalid("Chatbot.UnknownQuickReply", "That option is not available at this step.");
}
