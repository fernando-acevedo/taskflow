using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Domain.ApiKeys;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

internal sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("ApiKeys");
        builder.Property(key => key.Id).ValueGeneratedNever();
        builder.Property(key => key.Name).HasMaxLength(ApiKey.NameMaxLength);
        builder.Property(key => key.Prefix).HasMaxLength(ApiKey.DisplayPrefixLength).IsUnicode(false);
        builder.Property(key => key.KeyHash).HasMaxLength(ApiKey.HashLength).IsUnicode(false).IsFixedLength();
        builder.Property(key => key.Scope).HasConversion<string>().HasMaxLength(10).IsUnicode(false);

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(key => key.UserId).OnDelete(DeleteBehavior.Restrict);

        // The authentication lookup finds the key by hash alone, before any tenant is known.
        builder.HasIndex(key => key.KeyHash).IsUnique();
        builder.HasIndex(key => key.UserId);
    }
}
