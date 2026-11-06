using Microsoft.EntityFrameworkCore;
using TaskFlow.Domain.Activity;
using TaskFlow.Domain.ApiKeys;
using TaskFlow.Domain.Clients;
using TaskFlow.Domain.Notifications;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;
using TaskFlow.Domain.Tenancy;

namespace TaskFlow.Application.Abstractions;

/// <summary>
/// The business tables, implemented by the EF Core context in Infrastructure. EF Core already is a unit of
/// work over repositories, so handlers query these sets directly (ADR 0006).
/// </summary>
public interface IAppDbContext
{
    /// <summary>Not tenant-filtered: always constrain by the tenant or the user explicitly (ADR 0012).</summary>
    DbSet<Tenant> Tenants { get; }

    /// <summary>Not tenant-filtered: always constrain by the tenant or the user explicitly (ADR 0012).</summary>
    DbSet<TenantMembership> TenantMemberships { get; }

    /// <summary>Not tenant-filtered: always constrain by the tenant explicitly.</summary>
    DbSet<TenantSupportAccess> TenantSupportAccesses { get; }

    DbSet<Client> Clients { get; }

    DbSet<Project> Projects { get; }

    DbSet<Label> Labels { get; }

    DbSet<TaskItem> Tasks { get; }

    DbSet<TaskDependency> TaskDependencies { get; }

    DbSet<TaskComment> TaskComments { get; }

    DbSet<TimeEntry> TimeEntries { get; }

    /// <summary>Users mentioned in task comments, for notifications.</summary>
    DbSet<TaskCommentMention> TaskCommentMentions { get; }

    DbSet<ActivityEntry> ActivityEntries { get; }

    DbSet<ApiKey> ApiKeys { get; }

    DbSet<Notification> Notifications { get; }

    DbSet<NotificationPreferences> NotificationPreferences { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
