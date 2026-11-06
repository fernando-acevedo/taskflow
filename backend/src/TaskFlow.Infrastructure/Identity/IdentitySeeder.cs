using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Tenancy;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.Infrastructure.Tenancy;

namespace TaskFlow.Infrastructure.Identity;

public sealed class AdminSeedOptions
{
    public const string SectionName = "Seed:Admin";

    public string? Email { get; set; }

    public string? Password { get; set; }

    public string DisplayName { get; set; } = "Administrator";
}

/// <summary>
/// Creates the Admin role (the super-admin, ADR 0013), the default tenant and, when configured, the first admin
/// account, who is also Owner of the default tenant. Safe to run on every start.
/// </summary>
internal sealed partial class IdentitySeeder(
    RoleManager<IdentityRole<Guid>> roleManager,
    UserManager<ApplicationUser> userManager,
    AppDbContext db,
    IOptions<AdminSeedOptions> options,
    TimeProvider time,
    ILogger<IdentitySeeder> logger)
{
    public async Task SeedAsync()
    {
        if (!await roleManager.RoleExistsAsync(Roles.Admin))
        {
            EnsureSucceeded(await roleManager.CreateAsync(new IdentityRole<Guid>(Roles.Admin)));
        }

        await EnsureDefaultTenantAsync();

        var seed = options.Value;
        if (string.IsNullOrWhiteSpace(seed.Email) || string.IsNullOrWhiteSpace(seed.Password))
        {
            return;
        }

        var admin = await userManager.FindByEmailAsync(seed.Email);
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                UserName = seed.Email,
                Email = seed.Email,
                EmailConfirmed = true,
                DisplayName = seed.DisplayName,
            };
            EnsureSucceeded(await userManager.CreateAsync(admin, seed.Password));
            LogAdminCreated(logger, seed.Email);
        }

        if (!await userManager.IsInRoleAsync(admin, Roles.Admin))
        {
            EnsureSucceeded(await userManager.AddToRoleAsync(admin, Roles.Admin));
        }

        // The seeded admin is a super-admin and also Owner of the default tenant, so they can work in it.
        if (!await db.TenantMemberships.AnyAsync(m => m.TenantId == TenantDefaults.DefaultId && m.UserId == admin.Id))
        {
            db.TenantMemberships.Add(TenantMembership.Create(TenantDefaults.DefaultId, admin.Id, TenantRole.Owner, time.GetUtcNow()));
            await db.SaveChangesAsync();
        }
    }

    private async Task EnsureDefaultTenantAsync()
    {
        // The tenant tables are not tenant-owned, so this needs no tenant in scope.
        if (!await db.Tenants.AnyAsync(tenant => tenant.Id == TenantDefaults.DefaultId))
        {
            db.Tenants.Add(TenantDefaults.CreateDefault(time.GetUtcNow()));
            await db.SaveChangesAsync();
            LogTenantCreated(logger, TenantDefaults.DefaultSlug);
        }
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Identity seeding failed: " + string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.Description}")));
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Created the default tenant {Slug}")]
    private static partial void LogTenantCreated(ILogger logger, string slug);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded admin account {Email}")]
    private static partial void LogAdminCreated(ILogger logger, string email);
}
