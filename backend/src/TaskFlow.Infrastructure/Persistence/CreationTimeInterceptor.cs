using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Infrastructure.Persistence;

/// <summary>
/// Stamps new users with the time from <see cref="TimeProvider"/>, like the domain entities get theirs. Identity
/// creates users itself (registration, invitations, seeding), so this happens on save rather than in a constructor.
/// </summary>
internal sealed class CreationTimeInterceptor(TimeProvider time) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        StampNewUsers(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        StampNewUsers(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void StampNewUsers(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<ApplicationUser>())
        {
            if (entry.State == EntityState.Added && entry.Entity.CreatedAt == default)
            {
                entry.Entity.CreatedAt = time.GetUtcNow();
            }
        }
    }
}
