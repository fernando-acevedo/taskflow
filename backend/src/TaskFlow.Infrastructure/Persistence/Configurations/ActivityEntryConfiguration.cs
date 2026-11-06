using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Domain.Activity;
using TaskFlow.Domain.Projects;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

internal sealed class ActivityEntryConfiguration : IEntityTypeConfiguration<ActivityEntry>
{
    public void Configure(EntityTypeBuilder<ActivityEntry> builder)
    {
        builder.ToTable("ActivityEntries");
        builder.Property(entry => entry.Id).ValueGeneratedNever();
        builder.Property(entry => entry.Sequence).ValueGeneratedOnAdd().UseIdentityColumn();
        builder.Property(entry => entry.Type).HasConversion<string>().HasMaxLength(40).IsUnicode(false);
        builder.Property(entry => entry.Changes).IsRequired();

        builder.HasOne<Project>().WithMany().HasForeignKey(entry => entry.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(entry => entry.ActorId).OnDelete(DeleteBehavior.Restrict);

        // TaskItemId has no foreign key on purpose: the entries of a deleted task must stay.
        builder.HasIndex(entry => new { entry.ProjectId, entry.OccurredAt, entry.Sequence }).IsDescending(false, true, true);
        builder.HasIndex(entry => new { entry.TaskItemId, entry.OccurredAt, entry.Sequence }).IsDescending(false, true, true);
    }
}
