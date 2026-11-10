using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

internal sealed class LabelConfiguration : IEntityTypeConfiguration<Label>
{
    public void Configure(EntityTypeBuilder<Label> builder)
    {
        builder.ToTable("Labels");
        builder.Property(label => label.Id).ValueGeneratedNever();
        builder.Property(label => label.RowVersion).IsRowVersion();
        builder.Property(label => label.Name).HasMaxLength(Label.NameMaxLength);
        builder.Property(label => label.Color).HasMaxLength(Label.ColorLength).IsFixedLength().IsUnicode(false);

        builder.HasOne<Project>().WithMany().HasForeignKey(label => label.ProjectId).OnDelete(DeleteBehavior.Restrict);

        // The default SQL Server collation is case-insensitive, so this also enforces the case-insensitive rule.
        builder.HasIndex(label => new { label.ProjectId, label.Name }).IsUnique();
    }
}
