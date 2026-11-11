using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Domain.Chatbot;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

internal sealed class ChatSessionConfiguration : IEntityTypeConfiguration<ChatSession>
{
    public void Configure(EntityTypeBuilder<ChatSession> builder)
    {
        builder.ToTable("ChatSessions");
        builder.Property(session => session.Id).ValueGeneratedNever();
        builder.Property(session => session.Locale).HasMaxLength(2).IsUnicode(false);
        builder.Property(session => session.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(session => session.CurrentIntent).HasMaxLength(ChatSession.MaxFieldLength).IsUnicode(false);
        builder.Property(session => session.CurrentStep).HasMaxLength(ChatSession.MaxFieldLength).IsUnicode(false);
        builder.Property(session => session.RowVersion).IsRowVersion();

        var slotsComparer = new ValueComparer<IReadOnlyDictionary<string, string>>(
            (left, right) => ChatSlots.Equal(left!, right!),
            slots => slots.Aggregate(0, (hash, pair) => HashCode.Combine(hash, pair.Key, pair.Value)),
            slots => new Dictionary<string, string>(slots));
        builder.Property(session => session.Slots)
            .HasConversion(
                slots => JsonSerializer.Serialize(slots, (JsonSerializerOptions?)null),
                json => (IReadOnlyDictionary<string, string>)(JsonSerializer.Deserialize<Dictionary<string, string>>(json, (JsonSerializerOptions?)null) ?? new Dictionary<string, string>()),
                slotsComparer)
            .HasColumnType("nvarchar(max)");

        builder.Metadata.FindNavigation(nameof(ChatSession.Messages))!.SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(session => session.Messages).WithOne().HasForeignKey(message => message.SessionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany<IntentLog>().WithOne().HasForeignKey(log => log.SessionId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(session => session.ExpiresAt);
        builder.HasIndex(session => session.Status);
    }
}

internal static class ChatSlots
{
    public static bool Equal(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value);
}

internal sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable("ChatMessages");
        builder.Property(message => message.Id).ValueGeneratedNever();
        builder.Property(message => message.Sender).HasConversion<string>().HasMaxLength(10).IsUnicode(false);
        builder.Property(message => message.Text).HasMaxLength(ChatMessage.TextMaxLength);
        builder.Property(message => message.QuickReplyId).HasMaxLength(ChatMessage.QuickReplyIdMaxLength);
        builder.HasIndex(message => new { message.SessionId, message.Sequence }).IsUnique();
    }
}

internal sealed class IntentLogConfiguration : IEntityTypeConfiguration<IntentLog>
{
    public void Configure(EntityTypeBuilder<IntentLog> builder)
    {
        builder.ToTable("IntentLogs");
        builder.Property(log => log.Id).ValueGeneratedNever();
        builder.Property(log => log.Source).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(log => log.Outcome).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(log => log.RedactedText).HasMaxLength(IntentLog.TextMaxLength);
        builder.Property(log => log.NormalizedText).HasMaxLength(IntentLog.TextMaxLength);
        builder.Property(log => log.PredictedIntent).HasMaxLength(IntentLog.NameMaxLength).IsUnicode(false);
        builder.Property(log => log.SecondIntent).HasMaxLength(IntentLog.NameMaxLength).IsUnicode(false);
        builder.Property(log => log.ReceivedLocale).HasMaxLength(2).IsUnicode(false);
        builder.Property(log => log.DetectedLanguage).HasMaxLength(IntentLog.LanguageMaxLength).IsUnicode(false);
        builder.Property(log => log.ModelVersion).HasMaxLength(IntentLog.NameMaxLength).IsUnicode(false);

        builder.HasIndex(log => log.SessionId);
        builder.HasIndex(log => log.CreatedAt);
        builder.HasIndex(log => log.PredictedIntent);
    }
}
