using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

internal sealed class TaskDependencyConfiguration : IEntityTypeConfiguration<TaskDependency>
{
    public void Configure(EntityTypeBuilder<TaskDependency> builder)
    {
        builder.ToTable("TaskDependencies");
        builder.HasKey(dependency => new { dependency.TaskItemId, dependency.DependsOnTaskId });

        // The relationship from TaskItem (cascade) is in TaskItemConfiguration. This one is Restrict: SQL Server allows a
        // single cascade path, so DeleteTaskHandler removes the rows of the tasks that depend on the deleted one.
        builder.HasOne<TaskItem>().WithMany().HasForeignKey(dependency => dependency.DependsOnTaskId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(dependency => dependency.DependsOnTaskId);
    }
}
