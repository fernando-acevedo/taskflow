using System.Linq.Expressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Activity;
using TaskFlow.Domain.ApiKeys;
using TaskFlow.Domain.Clients;
using TaskFlow.Domain.Notifications;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;
using TaskFlow.Domain.Tenancy;
using TaskFlow.Infrastructure.Demo;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Infrastructure.Persistence;

public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options,
    ITenantContext? tenant = null,
    ILogger<AppDbContext>? logger = null)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IAppDbContext
{
    /// <summary>Name of the shadow property every <see cref="ITenantOwned"/> entity carries (ADR 0012).</summary>
    public const string TenantIdProperty = "TenantId";

    /// <summary>Key of the automatic tenant filter among the entity's named query filters.</summary>
    public const string TenantFilterKey = "Tenant";

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();

    public DbSet<TenantSupportAccess> TenantSupportAccesses => Set<TenantSupportAccess>();

    public DbSet<Client> Clients => Set<Client>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<Label> Labels => Set<Label>();

    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    public DbSet<TaskDependency> TaskDependencies => Set<TaskDependency>();

    public DbSet<TaskComment> TaskComments => Set<TaskComment>();

    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();

    public DbSet<TaskCommentMention> TaskCommentMentions => Set<TaskCommentMention>();

    public DbSet<ActivityEntry> ActivityEntries => Set<ActivityEntry>();

    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<NotificationPreferences> NotificationPreferences => Set<NotificationPreferences>();

    public DbSet<DemoResetLog> DemoResetLogs => Set<DemoResetLog>();

    /// <summary>
    /// The tenant the query filters compare against; read by EF on every query. <c>null</c> matches no row
    /// (fail closed) and is logged, so an empty result caused by a missing tenant is not a silent bug.
    /// </summary>
    private Guid? CurrentTenantId
    {
        get
        {
            var id = tenant?.TenantId;
            if (id is null && logger is not null)
            {
                NoTenantWarning(logger, null);
            }

            return id;
        }
    }

    private static readonly Action<ILogger, Exception?> NoTenantWarning = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(1, nameof(NoTenantWarning)),
        "A query ran without a resolved tenant; tenant-owned tables return no rows.");

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        var tenantOwned = builder.Model.GetEntityTypes()
            .Where(entity => typeof(ITenantOwned).IsAssignableFrom(entity.ClrType))
            .Select(entity => entity.ClrType)
            .ToList();

        // The shadow property goes in first so the configurations can include it in their indexes.
        foreach (var clrType in tenantOwned)
        {
            var entity = builder.Entity(clrType);
            entity.Property<Guid>(TenantIdProperty);
            entity.HasIndex(TenantIdProperty);
        }

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var clrType in tenantOwned)
        {
            builder.Entity(clrType).HasQueryFilter(TenantFilterKey, TenantFilter(clrType));
        }
    }

    // e => EF.Property<Guid>(e, "TenantId") == (Guid?)this.CurrentTenantId. With no tenant the right side is
    // null, which equals no value of the non-nullable column.
    private LambdaExpression TenantFilter(Type entityType)
    {
        var entity = Expression.Parameter(entityType, "e");
        var stored = Expression.Convert(
            Expression.Call(typeof(EF), nameof(EF.Property), [typeof(Guid)], entity, Expression.Constant(TenantIdProperty)),
            typeof(Guid?));
        var current = Expression.Property(Expression.Constant(this), nameof(CurrentTenantId));
        return Expression.Lambda(Expression.Equal(stored, current), entity);
    }
}
