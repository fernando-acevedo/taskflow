using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Tenancy;

namespace TaskFlow.Infrastructure.Persistence;

/// <summary>
/// The write side of the tenant guarantee (ADR 0012). Added <see cref="ITenantOwned"/> rows get the current
/// tenant; adding a row that names another tenant, or modifying or deleting a row of another tenant (or moving
/// it), throws <see cref="TenantIsolationException"/>. With no tenant resolved every such save is rejected.
/// </summary>
public sealed class TenantSaveInterceptor(ITenantContext tenant) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return ValueTask.FromResult(result);
    }

    internal void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is not ITenantOwned || entry.State is EntityState.Unchanged or EntityState.Detached)
            {
                continue;
            }

            var current = tenant.TenantId
                ?? throw new TenantIsolationException(
                    $"Cannot save {entry.Metadata.ShortName()} without a tenant: no tenant was resolved for this scope.");
            var property = entry.Property(AppDbContext.TenantIdProperty);

            if (entry.State == EntityState.Added)
            {
                StampAdded(entry, property, current);
            }
            else
            {
                CheckExisting(entry, property, current);
            }
        }
    }

    private static void StampAdded(EntityEntry entry, PropertyEntry property, Guid current)
    {
        var named = (Guid)property.CurrentValue!;
        if (named == Guid.Empty)
        {
            property.CurrentValue = current;
        }
        else if (named != current)
        {
            throw new TenantIsolationException($"Cannot add a {entry.Metadata.ShortName()} to another tenant.");
        }
    }

    private static void CheckExisting(EntityEntry entry, PropertyEntry property, Guid current)
    {
        if ((Guid)property.OriginalValue! != current || (Guid)property.CurrentValue! != current)
        {
            throw new TenantIsolationException(
                $"Cannot change a {entry.Metadata.ShortName()} that belongs to another tenant.");
        }
    }
}
