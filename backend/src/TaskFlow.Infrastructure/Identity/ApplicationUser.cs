using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TaskFlow.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public const int DisplayNameMaxLength = 100;

    /// <summary>Null until the user sets one; reads fall back to the email's local part.</summary>
    public string? DisplayName { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Set on insert from the app's <see cref="TimeProvider"/> (see <c>CreationTimeInterceptor</c>).</summary>
    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(user => user.DisplayName).HasMaxLength(ApplicationUser.DisplayNameMaxLength);
        builder.Property(user => user.IsActive).HasDefaultValue(true);
    }
}
