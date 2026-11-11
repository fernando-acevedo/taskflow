namespace TaskFlow.Domain.Chatbot;

public enum ChatSender
{
    User,
    Bot,
}

/// <summary>One message of a <see cref="ChatSession"/>. The text is already redacted by the caller.</summary>
public sealed class ChatMessage
{
    public const int TextMaxLength = 1000;
    public const int QuickReplyIdMaxLength = 100;

    internal ChatMessage(Guid id, Guid sessionId, int sequence, ChatSender sender, string text, string? quickReplyId, DateTimeOffset createdAt)
    {
        Id = id;
        SessionId = sessionId;
        Sequence = sequence;
        Sender = sender;
        Text = text;
        QuickReplyId = quickReplyId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid SessionId { get; private set; }

    public int Sequence { get; private set; }

    public ChatSender Sender { get; private set; }

    public string Text { get; private set; }

    public string? QuickReplyId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
