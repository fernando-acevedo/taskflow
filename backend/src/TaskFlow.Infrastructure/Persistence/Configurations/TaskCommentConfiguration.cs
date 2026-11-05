using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Domain.Tasks;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

internal sealed class TaskCommentConfiguration : IEntityTypeConfiguration<TaskComment>
{
    public void Configure(EntityTypeBuilder<TaskComment> builder)
    {
        builder.ToTable("TaskComments");
        builder.Property(comment => comment.Id).ValueGeneratedNever();
        builder.Property(comment => comment.Body).HasMaxLength(TaskComment.BodyMaxLength);

        builder.HasOne<TaskItem>().WithMany().HasForeignKey(comment => comment.TaskItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(comment => comment.AuthorId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(comment => comment.Mentions).WithOne().HasForeignKey(mention => mention.TaskCommentId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(comment => comment.Mentions).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(comment => new { comment.TaskItemId, comment.CreatedAt });
    }
}

internal sealed class TaskCommentMentionConfiguration : IEntityTypeConfiguration<TaskCommentMention>
{
    public void Configure(EntityTypeBuilder<TaskCommentMention> builder)
    {
        builder.ToTable("TaskCommentMentions");
        builder.HasKey(mention => new { mention.TaskCommentId, mention.UserId });
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(mention => mention.UserId).OnDelete(DeleteBehavior.Restrict);

        // F5 (notifications) looks up the comments that mention a user.
        builder.HasIndex(mention => mention.UserId);
    }
}
