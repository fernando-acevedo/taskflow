using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Chatbot.Dialogue;
using TaskFlow.Application.Chatbot.KnowledgeBase;
using TaskFlow.Domain.Chatbot;
using TaskFlow.Domain.Common;
using KbOutcome = TaskFlow.Application.Chatbot.KnowledgeBase.ChatOutcome;
using LogOutcome = TaskFlow.Domain.Chatbot.ChatOutcome;

namespace TaskFlow.Application.Chatbot;

/// <summary>
/// One turn of the support chat. <paramref name="SessionId"/> is null (or unknown, closed or idle) to open a new
/// session in <paramref name="Locale"/>; later turns ignore the locale. Send <paramref name="Text"/> or
/// <paramref name="QuickReplyId"/> (the quick reply wins when both come); with neither, a new session answers with the
/// start step.
/// </summary>
public sealed record SendChatMessageCommand(Guid? SessionId, string Locale, string? Text, string? QuickReplyId);

public sealed record ChatQuickReplyDto(string Id, string Label, string? Url);

/// <param name="Url">Set when the quick reply the user clicked carries a url for the client to open.</param>
/// <param name="Outcome">Completed or Escalated when <paramref name="Ended"/>, else null.</param>
/// <param name="IsFallback">The text was not understood.</param>
public sealed record ChatReplyDto(
    string Text,
    IReadOnlyList<ChatQuickReplyDto> QuickReplies,
    string? Url,
    string StepId,
    bool Ended,
    KbOutcome? Outcome,
    bool IsFallback);

/// <param name="SessionId">The id to send back on the next turn (a new one when the session was opened or replaced).</param>
/// <param name="NewSession">The turn opened a session (first turn, or the previous one was unknown, closed or idle).</param>
public sealed record SendChatMessageResult(Guid SessionId, string Locale, bool NewSession, ChatReplyDto Reply);

public static class SendChatMessageErrors
{
    public static readonly Error EmptyMessage =
        Error.Invalid("Chatbot.EmptyMessage", "Write a message or choose an option.");

    public static readonly Error MessageTooLong =
        Error.Invalid("Chatbot.MessageTooLong", "The message is too long.");
}

public sealed class SendChatMessageHandler(
    IAppDbContext db,
    IKnowledgeBase kb,
    DialogueEngine engine,
    IIntentClassifier classifier,
    TextNormalizer normalizer,
    TimeProvider time)
{
    public async Task<Result<SendChatMessageResult>> HandleAsync(SendChatMessageCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var text = string.IsNullOrWhiteSpace(command.Text) ? null : command.Text.Trim();
        var quickReplyId = string.IsNullOrWhiteSpace(command.QuickReplyId) ? null : command.QuickReplyId.Trim();
        if (quickReplyId is not null)
        {
            text = null;
        }

        if (text is { Length: > ChatMessage.TextMaxLength })
        {
            return SendChatMessageErrors.MessageTooLong;
        }

        if (quickReplyId is { Length: > ChatMessage.QuickReplyIdMaxLength })
        {
            return DialogueErrors.UnknownQuickReply;
        }

        var now = time.GetUtcNow();
        var idle = TimeSpan.FromMinutes(kb.Settings.SessionIdleMinutes);
        var hasInput = text is not null || quickReplyId is not null;

        var session = command.SessionId is { } id
            ? await db.ChatSessions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            : null;
        if (session is not null && (session.Status != ChatSessionStatus.Active || session.IsExpired(now) || session.CurrentStep is null))
        {
            session.Expire();
            session = null;
        }

        var isNew = session is null;
        DialogueTurn? opening = null;
        if (session is null)
        {
            var started = ChatSession.Start(command.Locale, now, idle);
            if (!started.IsSuccess)
            {
                return started.Error;
            }

            var start = engine.Start(started.Value.Locale);
            if (!start.IsSuccess)
            {
                return start.Error;
            }

            session = started.Value;
            opening = start.Value;
            db.ChatSessions.Add(session);
        }
        else if (!hasInput)
        {
            return SendChatMessageErrors.EmptyMessage;
        }

        DialogueTurn turn;
        string? userText = null;
        string? redacted = null;
        string? normalized = null;
        IntentPrediction? prediction = null;
        if (!hasInput)
        {
            turn = opening!;
        }
        else
        {
            var state = opening?.State ?? new DialogueState(session.Locale, session.CurrentIntent, session.CurrentStep, session.ConsecutiveFallbacks);
            DialogueInput input;
            if (quickReplyId is not null)
            {
                input = DialogueInput.QuickReply(quickReplyId);
                userText = LabelOf(state, quickReplyId);
            }
            else
            {
                redacted = Truncate(PiiRedactor.Redact(text));
                normalized = Truncate(normalizer.Normalize(redacted));
                prediction = normalized.Length > 0 && classifier.IsLoaded ? classifier.Classify(normalized) : null;
                input = DialogueInput.Text(prediction);
                userText = redacted;
            }

            var advanced = engine.Advance(state, input);
            if (advanced.IsSuccess)
            {
                turn = advanced.Value;
            }
            else if (opening is not null && quickReplyId is not null)
            {
                // A stale button from a session that no longer exists: start over instead of failing.
                turn = opening;
                userText = null;
            }
            else
            {
                return advanced.Error;
            }
        }

        var persisted = Persist(session, turn, userText, quickReplyId, now, idle, out var userMessage);
        if (!persisted.IsSuccess)
        {
            return persisted.Error;
        }

        if (userText is not null && redacted is not null)
        {
            db.IntentLogs.Add(new IntentLog(
                session.Id,
                userMessage?.Id,
                IntentSource.Text,
                redacted,
                normalized!,
                prediction?.Top.Intent,
                prediction?.Top.Score,
                prediction?.Second?.Intent,
                prediction?.Second?.Score,
                command.Locale,
                LanguageGuesser.Guess(text),
                prediction?.ModelVersion,
                OutcomeOf(turn.Reply),
                now));
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Two turns of the same session raced; the other one won.
            return CommonErrors.Stale;
        }

        var reply = turn.Reply;
        return new SendChatMessageResult(
            session.Id,
            session.Locale,
            isNew,
            new ChatReplyDto(
                reply.Text,
                reply.QuickReplies.Select(r => new ChatQuickReplyDto(r.Id, r.Label, r.Url)).ToList(),
                reply.Url,
                reply.StepId,
                reply.Ended,
                reply.Outcome,
                reply.IsFallback));
    }

    private static LogOutcome OutcomeOf(DialogueReply reply) =>
        reply.Outcome == KbOutcome.Escalated ? LogOutcome.Escalated
        : reply.IsFallback ? LogOutcome.Fallback
        : LogOutcome.Answered;

    private static string Truncate(string value) =>
        value.Length > IntentLog.TextMaxLength ? value[..IntentLog.TextMaxLength] : value;

    private string LabelOf(DialogueState state, string quickReplyId)
    {
        var reply = state.Step is null ? null : kb.FindStep(state.Step)?.QuickReplies.FirstOrDefault(r => r.Id == quickReplyId);
        return reply is null ? quickReplyId : Truncate(kb.GetLabel(reply, state.Locale));
    }

    private static Result Persist(
        ChatSession session,
        DialogueTurn turn,
        string? userText,
        string? quickReplyId,
        DateTimeOffset now,
        TimeSpan idle,
        out ChatMessage? userMessage)
    {
        userMessage = null;
        if (userText is not null)
        {
            var added = session.AddMessage(ChatSender.User, userText, quickReplyId, now);
            if (!added.IsSuccess)
            {
                return added;
            }

            userMessage = added.Value;
        }

        var bot = session.AddMessage(ChatSender.Bot, Truncate(turn.Reply.Text), null, now);
        if (!bot.IsSuccess)
        {
            return bot;
        }

        if (turn.State.Ended)
        {
            return turn.State.Outcome == KbOutcome.Escalated ? session.Escalate(now) : session.Complete(now);
        }

        var moved = session.Advance(turn.State.Intent, turn.State.Step, session.Slots, now, idle);
        for (var i = 0; moved.IsSuccess && i < turn.State.ConsecutiveFallbacks; i++)
        {
            moved = session.RegisterFallback(now, idle);
        }

        return moved;
    }
}
