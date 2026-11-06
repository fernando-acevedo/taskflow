using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TaskFlow.Infrastructure.Demo;

/// <summary>One successful reset of the public demo. The latest row tells the scheduler whether a reset is due.</summary>
public sealed class DemoResetLog
{
    public Guid Id { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset CompletedAt { get; set; }
}

internal sealed class DemoResetLogConfiguration : IEntityTypeConfiguration<DemoResetLog>
{
    public void Configure(EntityTypeBuilder<DemoResetLog> builder)
    {
        builder.ToTable("DemoResetLogs");
        builder.Property(log => log.Id).ValueGeneratedNever();
        builder.HasIndex(log => log.CompletedAt);
    }
}
