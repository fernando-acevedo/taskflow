using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Domain.Tenancy;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants");
        builder.Property(tenant => tenant.Id).ValueGeneratedNever();
        builder.Property(tenant => tenant.Name).HasMaxLength(Tenant.NameMaxLength);
        builder.Property(tenant => tenant.Slug).HasMaxLength(Tenant.SlugMaxLength);
        builder.HasIndex(tenant => tenant.Slug).IsUnique();
    }
}

internal sealed class TenantMembershipConfiguration : IEntityTypeConfiguration<TenantMembership>
{
    public void Configure(EntityTypeBuilder<TenantMembership> builder)
    {
        builder.ToTable("TenantMemberships");
        builder.HasKey(membership => new { membership.TenantId, membership.UserId });
        builder.Property(membership => membership.Role).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(membership => membership.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(membership => membership.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(membership => membership.UserId);
    }
}
