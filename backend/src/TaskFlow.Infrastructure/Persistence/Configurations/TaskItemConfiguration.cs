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
        builder.Property(task => task.ExternalKey).HasMaxLength(TaskItem.ExternalKeyMaxLength);
        builder.Property(task => task.StatusNote).HasMaxLength(TaskItem.StatusNoteMaxLength);
        builder.Property(task => task.IsBlocked).HasDefaultValue(false);

        // Deleting a task deletes what it depends on (cascade); the tasks that depend on it are cleared by DeleteTaskHandler
        // (SQL Server allows only one cascade path between two tables).
        builder.HasMany(task => task.Dependencies).WithOne().HasForeignKey(dependency => dependency.TaskItemId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(task => task.Dependencies).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Project>().WithMany().HasForeignKey(task => task.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(task => task.AssigneeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(task => task.CreatedById).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(task => task.Labels).WithMany().UsingEntity("TaskLabels");
        builder.Navigation(task => task.Labels).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(task => new { task.ProjectId, task.Status, task.Position });
        builder.HasIndex(task => task.AssigneeId);

        // The external key identifies a task in the project for whoever syncs it; many tasks have none.
        builder.HasIndex(task => new { task.ProjectId, task.ExternalKey }).IsUnique().HasFilter("[ExternalKey] IS NOT NULL");
    }
}
