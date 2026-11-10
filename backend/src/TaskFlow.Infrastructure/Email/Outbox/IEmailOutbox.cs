using System.Text.Json;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Infrastructure.Email.Outbox;

/// <summary>
/// Where emails go instead of being sent in the request (ADR 0018). <see cref="Enqueue"/> only adds the row to the
/// context, so the caller's <c>SaveChangesAsync</c> stores it with the change that caused the email; code with no
/// such save of its own uses <see cref="EnqueueAndSaveAsync"/>.
/// </summary>
public interface IEmailOutbox
{
    void Enqueue(string kind, EmailMessage message);

    Task EnqueueAndSaveAsync(string kind, EmailMessage message, CancellationToken cancellationToken);
}

internal sealed class EmailOutbox(AppDbContext db, TimeProvider time) : IEmailOutbox
{
    public void Enqueue(string kind, EmailMessage message)
    {
        var now = time.GetUtcNow();
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.CreateVersion7(now),
            Kind = kind,
            Payload = JsonSerializer.Serialize(message),
            CreatedAt = now,
            NextAttemptAt = now,
        });
    }

    public async Task EnqueueAndSaveAsync(string kind, EmailMessage message, CancellationToken cancellationToken)
    {
        Enqueue(kind, message);
        await db.SaveChangesAsync(cancellationToken);
    }
}
