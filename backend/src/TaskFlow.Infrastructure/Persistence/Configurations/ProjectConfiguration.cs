using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Domain.Clients;
using TaskFlow.Domain.Projects;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("Projects");
        builder.Property(project => project.Id).ValueGeneratedNever();
        builder.Property(project => project.Name).HasMaxLength(Project.NameMaxLength);
        builder.Property(project => project.Description).HasMaxLength(Project.DescriptionMaxLength);
        builder.Property(project => project.RowVersion).IsRowVersion();

        builder.HasOne<Client>().WithMany().HasForeignKey(project => project.ClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(project => new { project.ClientId, project.Name }).IsUnique();

        builder.HasMany(project => project.Members).WithOne().HasForeignKey(member => member.ProjectId);
        builder.Navigation(project => project.Members).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ProjectMemberConfiguration : IEntityTypeConfiguration<ProjectMember>
{
    public void Configure(EntityTypeBuilder<ProjectMember> builder)
    {
        builder.ToTable("ProjectMembers");
        builder.HasKey(member => new { member.ProjectId, member.UserId });
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(member => member.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
