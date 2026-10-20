using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskFlow.Application.Users;

namespace TaskFlow.Infrastructure.Identity;

public sealed class AdminSeedOptions
{
    public const string SectionName = "Seed:Admin";

    public string? Email { get; set; }

    public string? Password { get; set; }

    public string DisplayName { get; set; } = "Administrator";
}

/// <summary>Creates the Admin role and, when configured, the first admin account. Safe to run on every start.</summary>
internal sealed partial class IdentitySeeder(
    RoleManager<IdentityRole<Guid>> roleManager,
    UserManager<ApplicationUser> userManager,
    IOptions<AdminSeedOptions> options,
    ILogger<IdentitySeeder> logger)
{
    public async Task SeedAsync()
    {
        if (!await roleManager.RoleExistsAsync(Roles.Admin))
        {
            EnsureSucceeded(await roleManager.CreateAsync(new IdentityRole<Guid>(Roles.Admin)));
        }

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
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Identity seeding failed: " + string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.Description}")));
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded admin account {Email}")]
    private static partial void LogAdminCreated(ILogger logger, string email);
}
