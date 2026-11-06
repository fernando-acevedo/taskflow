using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Domain.Notifications;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");
        builder.Property(notification => notification.Id).ValueGeneratedNever();
        builder.Property(notification => notification.Type).HasConversion<string>().HasMaxLength(40).IsUnicode(false);
        builder.Property(notification => notification.Data).IsRequired();
        builder.Ignore(notification => notification.IsRead);

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(notification => notification.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(notification => notification.ActorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Project>().WithMany().HasForeignKey(notification => notification.ProjectId).OnDelete(DeleteBehavior.Restrict);

        // A deleted task takes its notifications with it; the project side is Restrict, so there is no second cascade path.
        builder.HasOne<TaskItem>().WithMany().HasForeignKey(notification => notification.TaskItemId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(notification => new { notification.UserId, notification.ReadAt });
        builder.HasIndex(notification => new { notification.UserId, notification.CreatedAt }).IsDescending(false, true);
        builder.HasIndex(notification => notification.TaskItemId);
    }
}

internal sealed class NotificationPreferencesConfiguration : IEntityTypeConfiguration<NotificationPreferences>
{
    public void Configure(EntityTypeBuilder<NotificationPreferences> builder)
    {
        builder.ToTable("NotificationPreferences");
        builder.HasKey(preferences => preferences.UserId);
        builder.Property(preferences => preferences.UserId).ValueGeneratedNever();
        builder.HasOne<ApplicationUser>().WithOne().HasForeignKey<NotificationPreferences>(preferences => preferences.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
