using System.Data;
using System.Data.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Demo;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tenancy;
using TaskFlow.Infrastructure.Identity;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.Infrastructure.Tenancy;

namespace TaskFlow.Infrastructure.Demo;

/// <summary>
/// The work of the demo reset, run with the demo tenant in scope (ADR 0005, 0014): the demo tenant's business data
/// and member users are deleted, the demo accounts are recreated with their configured passwords and made members
/// of the demo tenant, and a realistic agency dataset is added. Rows of every other tenant are never touched. All
/// in one transaction, under an application lock so two instances never reset at the same time.
/// </summary>
internal sealed partial class DemoResetWork(
    AppDbContext db,
    ITenantContext tenant,
    UserManager<ApplicationUser> userManager,
    SessionValidator sessions,
    TenantAccess tenantAccess,
    IOptions<DemoOptions> demoOptions,
    IOptions<AdminSeedOptions> adminSeed,
    TimeProvider time,
    ILogger<DemoResetWork> logger)
{
    private const string LockResource = "taskflow-demo-reset";

    /// <summary>Runs inside the demo tenant's <see cref="TenantScope"/>; returns false when it is not due or another instance is resetting.</summary>
    public async Task<bool> RunAsync(bool onlyIfDue, CancellationToken cancellationToken)
    {
        var options = demoOptions.Value;
        if (!options.Enabled)
        {
            return false;
        }

        // Defense in depth: whatever scope this runs in, it only ever works on a tenant flagged as the demo.
        var demoId = tenant.TenantId ?? throw new InvalidOperationException("The demo reset needs the demo tenant in scope.");
        if (!await db.Tenants.AnyAsync(row => row.Id == demoId && row.IsDemo, cancellationToken))
        {
            throw new InvalidOperationException("The demo reset refuses to touch a tenant that is not flagged IsDemo.");
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

            await EnsureAccountsAreSafeAsync(options, demoId, cancellationToken);
            await DeleteBusinessDataAsync(demoId, cancellationToken);
            await DeleteDemoUsersAsync(demoId, options, affectedUsers, cancellationToken);
            var accounts = await EnsureAccountsAsync(options, affectedUsers);
            var teammates = await CreateTeammatesAsync(affectedUsers);
            AddMemberships(demoId, accounts, teammates, startedAt);

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
                await sessions.ForgetAsync(userId, cancellationToken);
                await tenantAccess.ForgetAsync(userId, cancellationToken);
            }
        }

        return reset;
    }

    /// <summary>The demo "Admin" account owns the demo tenant, the other accounts and the teammates are members.</summary>
    private void AddMemberships(Guid demoId, List<DemoUser> accounts, List<Guid> teammates, DateTimeOffset now)
    {
        foreach (var account in accounts)
        {
            db.TenantMemberships.Add(TenantMembership.Create(demoId, account.Id, account.IsAdmin ? TenantRole.Owner : TenantRole.Member, now));
        }

        foreach (var teammate in teammates)
        {
            db.TenantMemberships.Add(TenantMembership.Create(demoId, teammate, TenantRole.Member, now));
        }
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

    /// <summary>
    /// Deletes the demo tenant's rows, children before parents; most foreign keys are Restrict. The tenant query
    /// filter (the work runs inside the demo tenant's scope) is what keeps every statement to that tenant. The
    /// TaskLabels join rows go with their tasks (cascade).
    /// </summary>
    private async Task DeleteBusinessDataAsync(Guid demoId, CancellationToken cancellationToken)
    {
        await db.Notifications.ExecuteDeleteAsync(cancellationToken);
        await db.ActivityEntries.ExecuteDeleteAsync(cancellationToken);
        await db.ApiKeys.ExecuteDeleteAsync(cancellationToken);
        await db.TimeEntries.ExecuteDeleteAsync(cancellationToken);
        await db.TaskCommentMentions.ExecuteDeleteAsync(cancellationToken);
        await db.TaskComments.ExecuteDeleteAsync(cancellationToken);
        await db.TaskDependencies.ExecuteDeleteAsync(cancellationToken);
        await db.Tasks.ExecuteDeleteAsync(cancellationToken);
        await db.Labels.ExecuteDeleteAsync(cancellationToken);
        await db.Set<ProjectMember>().ExecuteDeleteAsync(cancellationToken);
        await db.Projects.ExecuteDeleteAsync(cancellationToken);
        await db.Clients.ExecuteDeleteAsync(cancellationToken);

        // Preferences are per user, not per tenant: only those of the demo tenant's members go.
        // Users who also belong to another tenant keep theirs.
        await db.NotificationPreferences
            .Where(preferences => db.TenantMemberships.Any(m => m.TenantId == demoId && m.UserId == preferences.UserId))
            .Where(preferences => !db.TenantMemberships.Any(m => m.TenantId != demoId && m.UserId == preferences.UserId))
            .ExecuteDeleteAsync(cancellationToken);
        await db.TenantSupportAccesses.Where(access => access.TenantId == demoId).ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// Removes the demo tenant's members except the demo accounts and the seeded admin, and the memberships
    /// themselves (the accounts get theirs back). Users who also belong to another tenant are never deleted.
    /// Identity rows cascade.
    /// </summary>
    private async Task DeleteDemoUsersAsync(Guid demoId, DemoOptions options, HashSet<Guid> affectedUsers, CancellationToken cancellationToken)
    {
        var keep = KeptEmails(options);
        var doomed = await db.Users
            .Where(user => db.TenantMemberships.Any(m => m.TenantId == demoId && m.UserId == user.Id))
            .Where(user => !db.TenantMemberships.Any(m => m.TenantId != demoId && m.UserId == user.Id))
            .Where(user => user.NormalizedEmail == null || !keep.Contains(user.NormalizedEmail))
            .Select(user => user.Id)
            .ToListAsync(cancellationToken);
        affectedUsers.UnionWith(doomed);
        await db.TenantMemberships.Where(m => m.TenantId == demoId).ExecuteDeleteAsync(cancellationToken);
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

    /// <summary>
    /// A demo account is public: its password is published. If an account with a demo email already is a platform
    /// super-admin (Identity role Admin) or belongs to a tenant other than the demo, it is a real account that
    /// someone else registered under that email, so the reset aborts (rolled back, nothing changed) instead of
    /// overwriting its password or deleting its data (ADR 0014).
    /// </summary>
    private async Task EnsureAccountsAreSafeAsync(DemoOptions options, Guid demoId, CancellationToken cancellationToken)
    {
        foreach (var account in options.Accounts)
        {
            var email = account.Email!.Trim();
            if (await userManager.FindByEmailAsync(email) is not { } user)
            {
                continue;
            }

            var isSuperAdmin = await userManager.IsInRoleAsync(user, Roles.Admin);
            var otherTenants = await db.TenantMemberships.AnyAsync(m => m.UserId == user.Id && m.TenantId != demoId, cancellationToken);
            if (isSuperAdmin || otherTenants)
            {
                LogUnsafeAccount(logger, email, isSuperAdmin, otherTenants);
                throw new InvalidOperationException(
                    $"The demo reset aborted: the demo account '{email}' is a platform administrator or belongs to another tenant.");
            }
        }
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Demo reset aborted: account {Email} (super-admin: {IsSuperAdmin}, other tenants: {OtherTenants}) is not a demo-only account")]
    private static partial void LogUnsafeAccount(ILogger logger, string email, bool isSuperAdmin, bool otherTenants);

    /// <summary>Creates or repairs each demo account: password, active, unlocked, and a fresh security stamp.</summary>
    private async Task<List<DemoUser>> EnsureAccountsAsync(DemoOptions options, HashSet<Guid> affectedUsers)
    {
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

/// <summary>
/// Resets the public demo (ADR 0005, 0014). The demo is a tenant of its own (<c>IsDemo</c>): this creates it when
/// missing and runs <see cref="DemoResetWork"/> inside its <see cref="TenantScope"/>, so the reset can only reach
/// that tenant and production tenants can share the database.
/// </summary>
public sealed class DemoResetter(AppDbContext db, TenantScope tenantScope, IOptions<DemoOptions> demoOptions, TimeProvider time)
{
    /// <summary>Resets when a scheduled reset has passed since the last one. Returns whether it reset.</summary>
    public Task<bool> ResetIfDueAsync(CancellationToken cancellationToken) => RunAsync(onlyIfDue: true, cancellationToken);

    /// <summary>Resets now, whatever the schedule says. Returns false only when another instance is resetting.</summary>
    public Task<bool> ResetAsync(CancellationToken cancellationToken) => RunAsync(onlyIfDue: false, cancellationToken);

    private async Task<bool> RunAsync(bool onlyIfDue, CancellationToken cancellationToken)
    {
        if (!demoOptions.Value.Enabled)
        {
            return false;
        }

        await EnsureTenantAsync(cancellationToken);
        return await tenantScope.RunAsync(
            TenantDefaults.DemoId,
            (services, token) => services.GetRequiredService<DemoResetWork>().RunAsync(onlyIfDue, token),
            cancellationToken);
    }

    private async Task EnsureTenantAsync(CancellationToken cancellationToken)
    {
        var existing = await db.Tenants
            .Where(tenant => tenant.Id == TenantDefaults.DemoId)
            .Select(tenant => (bool?)tenant.IsDemo)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing == false)
        {
            throw new InvalidOperationException("The demo tenant id belongs to a tenant that is not flagged IsDemo.");
        }

        if (existing is null)
        {
            db.Tenants.Add(TenantDefaults.CreateDemo(time.GetUtcNow()));
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Another instance may have created it first; anything else is a real failure.
                if (!await TenantExistsAsync(cancellationToken))
                {
                    throw;
                }
            }
        }
    }

    private async Task<bool> TenantExistsAsync(CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        return await db.Tenants.AnyAsync(tenant => tenant.Id == TenantDefaults.DemoId, cancellationToken);
    }
}