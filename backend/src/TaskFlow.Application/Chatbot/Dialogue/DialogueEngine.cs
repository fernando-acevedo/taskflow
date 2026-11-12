using TaskFlow.Application.Chatbot.KnowledgeBase;
using TaskFlow.Domain.Chatbot;
using ChatOutcome = TaskFlow.Application.Chatbot.KnowledgeBase.ChatOutcome;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Chatbot.Dialogue;

/// <summary>
/// The V1 scripted dialogue (ADR 0026): pure, no persistence and no classifier. A turn is a quick reply (its
/// <c>next</c> step, or its <c>intent</c>'s entry step) or free text with an <see cref="IntentPrediction"/>. Free text
/// is classified wherever the user types (the script invites it at start, fallback and expects-text steps, but typing
/// instead of clicking works at any step). An accepted intent (score at least ConfidenceThreshold and ahead of the
/// second by MinMargin) goes to its entry step; otherwise the fallback step is shown, and once
/// MaxConsecutiveFallbacks fallbacks have been shown in a row the next miss escalates. Any successful step resets
/// the streak. End steps close the conversation with their outcome.
/// </summary>
public sealed class DialogueEngine(IKnowledgeBase kb, LocalizedReplyProvider replies)
{
    /// <summary>Opens a conversation at the start step. The locale is fixed from now on.</summary>
    public Result<DialogueTurn> Start(string locale)
    {
        if (!SupportedLocales.IsSupported(locale) || !kb.Settings.Locales.Contains(locale))
        {
            return ChatbotErrors.InvalidLocale;
        }

        var state = new DialogueState(locale, null, null, 0);
        return Show(state, kb.Settings.StartStep, null, null, false, null, 0);
    }

    public Result<DialogueTurn> Advance(DialogueState state, DialogueInput input)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(input);
        if (state.Ended || state.Step is null)
        {
            return ChatbotErrors.SessionClosed;
        }

        return input switch
        {
            QuickReplyInput q => OnQuickReply(state, q.Id),
            TextInput t => OnText(state, t.Prediction),
            _ => throw new ArgumentOutOfRangeException(nameof(input)),
        };
    }

    private Result<DialogueTurn> OnQuickReply(DialogueState state, string id)
    {
        var step = kb.FindStep(state.Step!);
        var reply = step?.QuickReplies.FirstOrDefault(r => r.Id == id);
        if (step is null || reply is null || replies.VisibleQuickReplies(step, state.Locale).All(v => v.Id != id))
        {
            return DialogueErrors.UnknownQuickReply;
        }

        var url = replies.ResolveUrl(reply);
        if (reply.Intent is not null)
        {
            var entry = kb.Intents.FirstOrDefault(i => i.Intent == reply.Intent)?.EntryStep;
            if (!string.IsNullOrEmpty(entry))
            {
                return Show(state, entry, reply.Intent, url, false, null, 0);
            }
        }

        // A url-only button keeps the user where they are; the client opens the url.
        return Show(state, reply.Next ?? state.Step!, state.Intent, url, false, null, 0);
    }

    private Result<DialogueTurn> OnText(DialogueState state, IntentPrediction? prediction)
    {
        var intent = Accept(prediction);
        var entry = intent is null ? null : kb.Intents.FirstOrDefault(i => i.Intent == intent)?.EntryStep;
        if (!string.IsNullOrEmpty(entry))
        {
            return Show(state, entry, intent, null, false, intent, 0);
        }

        return state.ConsecutiveFallbacks >= kb.Settings.MaxConsecutiveFallbacks
            ? Show(state, kb.Settings.EscalateStep, state.Intent, null, true, null, state.ConsecutiveFallbacks)
            : Show(state, kb.Settings.FallbackStep, state.Intent, null, true, null, state.ConsecutiveFallbacks + 1);
    }

    private string? Accept(IntentPrediction? prediction)
    {
        if (prediction is null || prediction.Ranked.Count == 0)
        {
            return null;
        }

        var top = prediction.Top;
        var margin = top.Score - (prediction.Second?.Score ?? 0);
        return top.Score >= kb.Settings.ConfidenceThreshold && margin >= kb.Settings.MinMargin - 1e-9 ? top.Intent : null;
    }

    private Result<DialogueTurn> Show(
        DialogueState state, string stepId, string? intent, string? url, bool isFallback, string? matched, int fallbacks)
    {
        var step = kb.FindStep(stepId)
                   ?? throw new InvalidOperationException($"The knowledge base has no step '{stepId}'.");
        var ended = step.End;
        var outcome = ended ? step.Outcome ?? ChatOutcome.Completed : (ChatOutcome?)null;
        var next = state with
        {
            Intent = intent,
            Step = ended ? null : stepId,
            ConsecutiveFallbacks = fallbacks,
            Ended = ended,
            Outcome = outcome,
        };
        var reply = new DialogueReply(
            replies.PickText(step, state.Locale),
            ended ? [] : replies.VisibleQuickReplies(step, state.Locale),
            url,
            stepId,
            ended,
            outcome,
            isFallback,
            matched);
        return new DialogueTurn(next, reply);
    }
}
