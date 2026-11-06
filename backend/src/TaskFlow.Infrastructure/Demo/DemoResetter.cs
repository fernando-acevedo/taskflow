using System.Data;
using System.Data.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using TaskFlow.Application.Demo;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Projects;
using TaskFlow.Infrastructure.Identity;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Infrastructure.Demo;

/// <summary>
/// Wipes the public demo and seeds it again (ADR 0005): business data and non-demo users are deleted, the demo
/// accounts are recreated with their configured passwords, and a realistic agency dataset is added. All in one
/// transaction, under an application lock so two instances never reset at the same time.
/// </summary>
public sealed class DemoResetter(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    SessionValidator sessions,
    IOptions<DemoOptions> demoOptions,
    IOptions<AdminSeedOptions> adminSeed,
    TimeProvider time)
{
    private const string LockResource = "taskflow-demo-reset";

    /// <summary>Resets when a scheduled reset has passed since the last one. Returns whether it reset.</summary>
    public Task<bool> ResetIfDueAsync(CancellationToken cancellationToken) => RunAsync(onlyIfDue: true, cancellationToken);

    /// <summary>Resets now, whatever the schedule says. Returns false only when another instance is resetting.</summary>
    public Task<bool> ResetAsync(CancellationToken cancellationToken) => RunAsync(onlyIfDue: false, cancellationToken);

    private async Task<bool> RunAsync(bool onlyIfDue, CancellationToken cancellationToken)
    {
        var options = demoOptions.Value;
        if (!options.Enabled)
        {
            return false;
        }

        var affectedUsers = new HashSet<Guid>();
        var strategy = db.Database.CreateExecutionStrategy();
        var reset = await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            affectedUsers.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            if (!await TryAcquireLockAsync(transaction, cancellationToken))
            {
                return false;
            }

            var startedAt = time.GetUtcNow();
            if (onlyIfDue)
            {
                // Read inside the lock: another instance may have just finished a reset.
                var last = await db.Set<DemoResetLog>()
                    .OrderByDescending(log => log.CompletedAt)
                    .Select(log => (DateTimeOffset?)log.CompletedAt)
                    .FirstOrDefaultAsync(cancellationToken);
                if (!DemoSchedule.IsDue(options.ResetCron, options.TimeZone, last, startedAt))
                {
                    return false;
                }
            }

            await DeleteBusinessDataAsync(cancellationToken);
            await DeleteOtherUsersAsync(options, affectedUsers, cancellationToken);
            var accounts = await EnsureAccountsAsync(options, affectedUsers);
            var teammates = await CreateTeammatesAsync(affectedUsers);

            DemoDataSeeder.Seed(db, accounts, teammates, startedAt);
            db.Set<DemoResetLog>().Add(new DemoResetLog
            {
                Id = Guid.CreateVersion7(startedAt),
                StartedAt = startedAt,
                CompletedAt = time.GetUtcNow(),
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        });

        if (reset)
        {
            foreach (var userId in affectedUsers)
            {
                sessions.Forget(userId);
            }
        }

        return reset;
    }

    /// <summary>Takes a transaction-owned lock without waiting; it is released when the transaction ends.</summary>
    private async Task<bool> TryAcquireLockAsync(IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "sp_getapplock";
        command.CommandType = CommandType.StoredProcedure;
        AddParameter(command, "@Resource", LockResource);
        AddParameter(command, "@LockMode", "Exclusive");
        AddParameter(command, "@LockOwner", "Transaction");
        AddParameter(command, "@LockTimeout", 0);
        var result = command.CreateParameter();
        result.ParameterName = "@Result";
        result.Direction = ParameterDirection.ReturnValue;
        result.DbType = DbType.Int32;
        command.Parameters.Add(result);

        await command.ExecuteNonQueryAsync(cancellationToken);
        return (int)result.Value! >= 0;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    /// <summary>Deletes children before parents; most foreign keys are Restrict.</summary>
    private async Task DeleteBusinessDataAsync(CancellationToken cancellationToken)
    {
        await db.Notifications.ExecuteDeleteAsync(cancellationToken);
        await db.ActivityEntries.ExecuteDeleteAsync(cancellationToken);
        await db.TimeEntries.ExecuteDeleteAsync(cancellationToken);
        await db.TaskCommentMentions.ExecuteDeleteAsync(cancellationToken);
        await db.TaskComments.ExecuteDeleteAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [TaskLabels]", cancellationToken);
        await db.Tasks.ExecuteDeleteAsync(cancellationToken);
        await db.Labels.ExecuteDeleteAsync(cancellationToken);
        await db.Set<ProjectMember>().ExecuteDeleteAsync(cancellationToken);
        await db.Projects.ExecuteDeleteAsync(cancellationToken);
        await db.Clients.ExecuteDeleteAsync(cancellationToken);
        await db.NotificationPreferences.ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>Removes every user except the demo accounts and the seeded admin. Identity rows cascade.</summary>
    private async Task DeleteOtherUsersAsync(DemoOptions options, HashSet<Guid> affectedUsers, CancellationToken cancellationToken)
    {
        var keep = KeptEmails(options);
        var doomed = await db.Users
            .Where(user => user.NormalizedEmail == null || !keep.Contains(user.NormalizedEmail))
            .Select(user => user.Id)
            .ToListAsync(cancellationToken);
        affectedUsers.UnionWith(doomed);
        await db.Users.Where(user => doomed.Contains(user.Id)).ExecuteDeleteAsync(cancellationToken);
    }

    private List<string> KeptEmails(DemoOptions options)
    {
        var emails = options.Accounts.Select(account => account.Email);
        if (!string.IsNullOrWhiteSpace(adminSeed.Value.Email))
        {
            emails = emails.Append(adminSeed.Value.Email);
        }

        return [.. emails.Where(email => !string.IsNullOrWhiteSpace(email)).Select(email => userManager.NormalizeEmail(email!.Trim())!)];
    }

    /// <summary>Creates or repairs each demo account: password, role, active, unlocked, and a fresh security stamp.</summary>
    private async Task<List<DemoUser>> EnsureAccountsAsync(DemoOptions options, HashSet<Guid> affectedUsers)
    {
        if (!await roleManager.RoleExistsAsync(Roles.Admin))
        {
            Ensure(await roleManager.CreateAsync(new IdentityRole<Guid>(Roles.Admin)));
        }

        var users = new List<DemoUser>();
        foreach (var account in options.Accounts)
        {
            var email = account.Email!.Trim();
            var user = await userManager.FindByEmailAsync(email);
            if (user is null)
            {
                user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = account.DisplayName };
                Ensure(await userManager.CreateAsync(user, account.Password!));
                Ensure(await userManager.SetLockoutEnabledAsync(user, false));
            }
            else
            {
                user.DisplayName = account.DisplayName;
                user.IsActive = true;
                user.EmailConfirmed = true;
                user.LockoutEnd = null;
                user.LockoutEnabled = false;
                user.PasswordHash = userManager.PasswordHasher.HashPassword(user, account.Password!);
                Ensure(await userManager.UpdateAsync(user));
                Ensure(await userManager.ResetAccessFailedCountAsync(user));
            }

            var isAdmin = await userManager.IsInRoleAsync(user, Roles.Admin);
            if (account.Role == DemoAccountRole.Admin && !isAdmin)
            {
                Ensure(await userManager.AddToRoleAsync(user, Roles.Admin));
            }
            else if (account.Role != DemoAccountRole.Admin && isAdmin)
            {
                Ensure(await userManager.RemoveFromRoleAsync(user, Roles.Admin));
            }

            Ensure(await userManager.UpdateSecurityStampAsync(user));
            affectedUsers.Add(user.Id);
            users.Add(new DemoUser(user.Id, account.Role == DemoAccountRole.Admin));
        }

        return users;
    }

    /// <summary>
    /// Fictional colleagues without a password (nobody can sign in as them), so boards and the workload dashboard
    /// show more than the demo accounts.
    /// </summary>
    private async Task<List<Guid>> CreateTeammatesAsync(HashSet<Guid> affectedUsers)
    {
        var ids = new List<Guid>();
        foreach (var (name, email) in DemoDataSeeder.Teammates)
        {
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = name };
            Ensure(await userManager.CreateAsync(user));
            ids.Add(user.Id);
            affectedUsers.Add(user.Id);
        }

        return ids;
    }

    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Demo reset failed: " + string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.Description}")));
        }
    }
}

/// <summary>A demo account as the seeder needs it.</summary>
internal sealed record DemoUser(Guid Id, bool IsAdmin);
