using Microsoft.EntityFrameworkCore;
using TaskFlow.Domain.Activity;
using TaskFlow.Domain.Clients;
using TaskFlow.Domain.Notifications;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Abstractions;

/// <summary>
/// The business tables, implemented by the EF Core context in Infrastructure. EF Core already is a unit of
/// work over repositories, so handlers query these sets directly (ADR 0006).
/// </summary>
public interface IAppDbContext
{
    DbSet<Client> Clients { get; }

    DbSet<Project> Projects { get; }

    DbSet<Label> Labels { get; }

    DbSet<TaskItem> Tasks { get; }

    DbSet<TaskComment> TaskComments { get; }

    DbSet<TimeEntry> TimeEntries { get; }

    /// <summary>Users mentioned in task comments, for notifications.</summary>
    DbSet<TaskCommentMention> TaskCommentMentions { get; }

    DbSet<ActivityEntry> ActivityEntries { get; }

    DbSet<Notification> Notifications { get; }

    DbSet<NotificationPreferences> NotificationPreferences { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
