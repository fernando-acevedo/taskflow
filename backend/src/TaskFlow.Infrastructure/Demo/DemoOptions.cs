using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using TaskFlow.Application.Demo;

namespace TaskFlow.Infrastructure.Demo;

public enum DemoAccountRole
{
    Member,
    Admin,
}

/// <summary>A shared account the login page shows to visitors. Passwords come from configuration only (app settings).</summary>
public sealed class DemoAccountOptions
{
    public string? Email { get; set; }

    public string? DisplayName { get; set; }

    public string? Password { get; set; }

    public DemoAccountRole Role { get; set; }
}

public sealed class DemoOptions
{
    public const string SectionName = "Demo";

    public bool Enabled { get; set; }

    /// <summary>
    /// Explicit consent to the reset of the demo tenant (ADR 0014): it deletes and reseeds that tenant's rows and
    /// never touches another tenant. Without it a
    /// demo that is switched on by mistake refuses to start.
    /// </summary>
    public bool AllowDataWipe { get; set; }

    /// <summary>Five-field cron expression, evaluated in <see cref="TimeZone"/>.</summary>
    public string ResetCron { get; set; } = "0 3 * * *";

    public string TimeZone { get; set; } = "UTC";

    /// <summary>How often the background service checks whether a reset is due.</summary>
    public int CheckIntervalMinutes { get; set; } = 15;

    public List<DemoAccountOptions> Accounts { get; set; } = [];
}

/// <summary>Fails startup when the demo is enabled but misconfigured. Does nothing when it is disabled.</summary>
internal sealed class DemoOptionsValidator(IOptions<IdentityOptions> identity) : IValidateOptions<DemoOptions>
{
    public ValidateOptionsResult Validate(string? name, DemoOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        if (!options.AllowDataWipe)
        {
            failures.Add("Demo:AllowDataWipe must be true when Demo:Enabled is true: it allows the daily reset of the demo tenant, which deletes and reseeds that tenant's data (other tenants are never touched).");
        }

        if (DemoSchedule.Validate(options.ResetCron, options.TimeZone) is { } scheduleProblem)
        {
            failures.Add($"Demo: {scheduleProblem}");
        }

        if (options.CheckIntervalMinutes < 1)
        {
            failures.Add("Demo:CheckIntervalMinutes must be at least 1.");
        }

        if (!options.Accounts.Exists(account => account.Role == DemoAccountRole.Admin))
        {
            failures.Add("Demo:Accounts needs at least one account with Role 'Admin'.");
        }

        if (!options.Accounts.Exists(account => account.Role == DemoAccountRole.Member))
        {
            failures.Add("Demo:Accounts needs at least one account with Role 'Member'.");
        }

        for (var i = 0; i < options.Accounts.Count; i++)
        {
            var account = options.Accounts[i];
            if (string.IsNullOrWhiteSpace(account.Email))
            {
                failures.Add($"Demo:Accounts:{i}:Email is required.");
            }

            if (string.IsNullOrWhiteSpace(account.Password))
            {
                failures.Add($"Demo:Accounts:{i}:Password is required (set it as an app setting, never in the repo).");
            }
            else if (PasswordProblem(account.Password, identity.Value.Password) is { } passwordProblem)
            {
                failures.Add($"Demo:Accounts:{i}:Password {passwordProblem}.");
            }
        }

        var emails = options.Accounts.Where(a => !string.IsNullOrWhiteSpace(a.Email)).Select(a => a.Email!.Trim().ToUpperInvariant());
        if (emails.GroupBy(email => email).Any(group => group.Count() > 1))
        {
            failures.Add("Demo:Accounts has duplicate emails.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>Checks the rules of Identity's password options (the reset would otherwise throw on every attempt).</summary>
    private static string? PasswordProblem(string password, PasswordOptions rules)
    {
        var problems = new List<string>();
        if (password.Length < rules.RequiredLength)
        {
            problems.Add($"is shorter than {rules.RequiredLength} characters");
        }

        if (rules.RequireDigit && !password.Any(char.IsAsciiDigit))
        {
            problems.Add("needs a digit");
        }

        if (rules.RequireLowercase && !password.Any(char.IsAsciiLetterLower))
        {
            problems.Add("needs a lowercase letter");
        }

        if (rules.RequireUppercase && !password.Any(char.IsAsciiLetterUpper))
        {
            problems.Add("needs an uppercase letter");
        }

        if (rules.RequireNonAlphanumeric && password.All(char.IsAsciiLetterOrDigit))
        {
            problems.Add("needs a non-alphanumeric character");
        }

        if (password.Distinct().Count() < rules.RequiredUniqueChars)
        {
            problems.Add($"needs at least {rules.RequiredUniqueChars} different characters");
        }

        return problems.Count == 0 ? null : "is not valid for the password rules: it " + string.Join(", ", problems);
    }
}
