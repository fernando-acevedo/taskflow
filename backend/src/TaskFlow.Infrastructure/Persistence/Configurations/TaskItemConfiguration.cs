using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

/// <summary>Status and priority stay integers so that ordering by them follows the board and severity order.</summary>
internal sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.ToTable("Tasks");
        builder.Property(task => task.Id).ValueGeneratedNever();
        builder.Property(task => task.Title).HasMaxLength(TaskItem.TitleMaxLength);
        builder.Property(task => task.Description).HasMaxLength(TaskItem.DescriptionMaxLength);
        builder.Property(task => task.RowVersion).IsRowVersion();

        builder.HasOne<Project>().WithMany().HasForeignKey(task => task.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(task => task.AssigneeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(task => task.CreatedById).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(task => new { task.ProjectId, task.Status, task.Position });
        builder.HasIndex(task => task.AssigneeId);
    }
}
