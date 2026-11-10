using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TaskFlow.Infrastructure.Email.Outbox;

/// <summary>
/// An email waiting to be sent (ADR 0018). Not tenant owned: the worker has no tenant, and the payload holds the
/// composed message (recipient, subject and bodies, already in the right language).
/// </summary>
public sealed class OutboxMessage
{
    public const int KindMaxLength = 40;
    public const int LastErrorMaxLength = 600;

    public Guid Id { get; set; }

    /// <summary>What produced it (<c>Notification</c>, <c>Invitation</c>, <c>PasswordReset</c>...), for inspection only.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The <see cref="EmailMessage"/> as JSON.</summary>
    public string Payload { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the message is next due; the creation time at first.</summary>
    public DateTimeOffset NextAttemptAt { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>Set when the last allowed attempt failed; the row is then left for inspection.</summary>
    public DateTimeOffset? FailedAt { get; set; }

    /// <summary>Counted when a worker claims the message, so a worker that crashes while sending still uses one.</summary>
    public int Attempts { get; set; }

    /// <summary>The type and message of the last failure. Never the payload.</summary>
    public string? LastError { get; set; }

    /// <summary>The claim of a worker expires at this time; after it another worker can take the message.</summary>
    public DateTimeOffset? LockedUntil { get; set; }

    /// <summary>Identifies the claim, so a worker whose lease expired can't overwrite the result of the next one.</summary>
    public Guid? LockToken { get; set; }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.Property(message => message.Id).ValueGeneratedNever();
        builder.Property(message => message.Kind).HasMaxLength(OutboxMessage.KindMaxLength).IsUnicode(false);
        builder.Property(message => message.Payload).IsRequired();
        builder.Property(message => message.LastError).HasMaxLength(OutboxMessage.LastErrorMaxLength);

        // The polling query: due rows that are neither sent nor failed.
        builder.HasIndex(message => message.NextAttemptAt)
            .HasFilter("[ProcessedAt] IS NULL AND [FailedAt] IS NULL");

        // The cleanup of old processed rows.
        builder.HasIndex(message => message.ProcessedAt)
            .HasFilter("[ProcessedAt] IS NOT NULL");
    }
}
