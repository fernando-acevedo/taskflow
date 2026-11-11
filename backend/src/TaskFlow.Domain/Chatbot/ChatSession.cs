using TaskFlow.Domain.Common;

namespace TaskFlow.Domain.Chatbot;

public enum ChatSessionStatus
{
    Active,
    Completed,
    Escalated,
    Expired,
}

/// <summary>
/// Aggregate root of a support chat. Not tenant-owned: sessions start before login (ADR 0026). Its id doubles
/// as the opaque session token the client sends back.
/// </summary>
public sealed class ChatSession
{
    public const int MaxFieldLength = 100;

    private readonly List<ChatMessage> _messages = [];

    private ChatSession()
    {
        Locale = SupportedLocales.English;
        Slots = new Dictionary<string, string>();
    }

    public Guid Id { get; private set; }

    public string Locale { get; private set; }

    public ChatSessionStatus Status { get; private set; }

    public string? CurrentIntent { get; private set; }

    public string? CurrentStep { get; private set; }

    public IReadOnlyDictionary<string, string> Slots { get; private set; }

    public int ConsecutiveFallbacks { get; private set; }

    /// <summary>Sequence of the last message added; the next one is this plus one, even when messages are not loaded.</summary>
    public int LastSequence { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset LastActivityAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>The Identity user id once the chat is tied to a signed-in user; null for anonymous sessions.</summary>
    public Guid? UserId { get; private set; }

    /// <summary>Optimistic concurrency token, maintained by the database.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<ChatMessage> Messages => _messages;

    public static Result<ChatSession> Start(string locale, DateTimeOffset now, TimeSpan idle)
    {
        if (!SupportedLocales.IsSupported(locale))
        {
            return ChatbotErrors.InvalidLocale;
        }

        return new ChatSession
        {
            Id = Guid.CreateVersion7(now),
            Locale = locale,
            Status = ChatSessionStatus.Active,
            CreatedAt = now,
            LastActivityAt = now,
            ExpiresAt = now + idle,
        };
    }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    public Result ChangeLocale(string locale)
    {
        if (Status != ChatSessionStatus.Active)
        {
            return ChatbotErrors.SessionClosed;
        }

        if (!SupportedLocales.IsSupported(locale))
        {
            return ChatbotErrors.InvalidLocale;
        }

        Locale = locale;
        return Result.Success();
    }

    /// <summary>Moves the dialogue to an intent/step with its collected slots, and resets the fallback streak.</summary>
    public Result Advance(string? intent, string? step, IReadOnlyDictionary<string, string> slots, DateTimeOffset now, TimeSpan idle)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (Status != ChatSessionStatus.Active)
        {
            return ChatbotErrors.SessionClosed;
        }

        CurrentIntent = intent;
        CurrentStep = step;
        Slots = new Dictionary<string, string>(slots);
        ConsecutiveFallbacks = 0;
        Touch(now, idle);
        return Result.Success();
    }

    public Result RegisterFallback(DateTimeOffset now, TimeSpan idle)
    {
        if (Status != ChatSessionStatus.Active)
        {
            return ChatbotErrors.SessionClosed;
        }

        ConsecutiveFallbacks++;
        Touch(now, idle);
        return Result.Success();
    }

    public Result Complete(DateTimeOffset now) => Close(ChatSessionStatus.Completed, now);

    public Result Escalate(DateTimeOffset now) => Close(ChatSessionStatus.Escalated, now);

    /// <summary>Marks an idle session expired; a session already closed keeps its status.</summary>
    public void Expire()
    {
        if (Status == ChatSessionStatus.Active)
        {
            Status = ChatSessionStatus.Expired;
        }
    }

    public Result<ChatMessage> AddMessage(ChatSender sender, string text, string? quickReplyId, DateTimeOffset now)
    {
        if (Status != ChatSessionStatus.Active)
        {
            return ChatbotErrors.SessionClosed;
        }

        LastSequence++;
        var message = new ChatMessage(Guid.CreateVersion7(now), Id, LastSequence, sender, text, quickReplyId, now);
        _messages.Add(message);
        return message;
    }

    private Result Close(ChatSessionStatus status, DateTimeOffset now)
    {
        if (Status != ChatSessionStatus.Active)
        {
            return ChatbotErrors.SessionClosed;
        }

        Status = status;
        CurrentIntent = null;
        CurrentStep = null;
        LastActivityAt = now;
        return Result.Success();
    }

    private void Touch(DateTimeOffset now, TimeSpan idle)
    {
        LastActivityAt = now;
        ExpiresAt = now + idle;
    }
}

/// <summary>The languages the chatbot answers in.</summary>
public static class SupportedLocales
{
    public const string English = "en";
    public const string Spanish = "es";

    public static bool IsSupported(string? locale) => locale is English or Spanish;
}

public static class ChatbotErrors
{
    public static readonly Error SessionClosed =
        Error.Conflict("Chatbot.SessionClosed", "This conversation has ended. Start a new one.");

    public static readonly Error InvalidLocale =
        Error.Invalid("Chatbot.InvalidLocale", "The language must be en or es.");
}
