using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Domain.Tasks;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

internal sealed class TimeEntryConfiguration : IEntityTypeConfiguration<TimeEntry>
{
    public void Configure(EntityTypeBuilder<TimeEntry> builder)
    {
        builder.ToTable("TimeEntries");
        builder.Property(entry => entry.Id).ValueGeneratedNever();
        builder.Property(entry => entry.Note).HasMaxLength(TimeEntry.NoteMaxLength);

        builder.HasOne<TaskItem>().WithMany().HasForeignKey(entry => entry.TaskItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(entry => entry.UserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entry => entry.TaskItemId);
        builder.HasIndex(entry => new { entry.UserId, entry.Date });
    }
}
