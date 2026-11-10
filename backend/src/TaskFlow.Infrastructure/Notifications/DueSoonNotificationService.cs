using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskFlow.Application.Notifications;
using TaskFlow.Infrastructure.Caching;
using TaskFlow.Infrastructure.Diagnostics;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.Infrastructure.Tenancy;

namespace TaskFlow.Infrastructure.Notifications;

public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>Turns the daily TaskDueSoon job off (the integration tests do, and call the handler themselves).</summary>
    public bool DueSoonEnabled { get; set; } = true;

    /// <summary>The hour of the day (UTC, 0 to 23) when the job runs.</summary>
    public int DueSoonHourUtc { get; set; } = 8;
}

/// <summary>
/// Runs <see cref="NotifyDueSoonHandler"/> for each tenant once a day at <see cref="NotificationOptions.DueSoonHourUtc"/>. If the
/// app starts after that hour it runs at once; the handler is idempotent, so a restart or a second instance
/// doesn't duplicate notifications (ADR 0010). That idempotency is why the last run is kept in memory and not persisted:
/// a restart repeats the pass at worst, and creates nothing new. Each pass also takes an application lock, so with
/// several instances only one runs it at a time (ADR 0020).
/// </summary>
internal sealed partial class DueSoonNotificationService(
    IServiceScopeFactory scopes,
    TenantScope tenantScope,
    JobLock jobLock,
    IOptions<NotificationOptions> options,
    TimeProvider time,
    ILogger<DueSoonNotificationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.DueSoonEnabled)
        {
            return;
        }

        var hour = Math.Clamp(options.Value.DueSoonHourUtc, 0, 23);
        DateOnly? lastRun = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = time.GetUtcNow();
            var today = DateOnly.FromDateTime(now.UtcDateTime);
            var slot = new DateTimeOffset(today.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero);
            if (now >= slot && lastRun != today)
            {
                lastRun = today;
                await RunAsync(stoppingToken);
                now = time.GetUtcNow();
            }

            var next = now >= slot ? slot.AddDays(1) : slot;
            try
            {
                await Task.Delay(next - now, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var activity = TaskFlowTelemetry.Source.StartActivity("due-soon-notifications");
        IAsyncDisposable? held;
        try
        {
            held = await jobLock.TryAcquireAsync("taskflow-due-soon", cancellationToken);
        }
#pragma warning disable CA1031 // A failed run must not stop the service; the next day (or restart) tries again.
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogFailed(logger, exception);
            return;
        }

        if (held is null)
        {
            activity?.SetTag("job.skipped", true);
            return;
        }

        await using (held)
        {
            await RunTenantsAsync(cancellationToken);
        }
    }

    private async Task RunTenantsAsync(CancellationToken cancellationToken)
    {
        List<Guid> tenants;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            tenants = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Tenants
                .AsNoTracking()
                .Select(tenant => tenant.Id)
                .ToListAsync(cancellationToken);
        }
#pragma warning disable CA1031 // A failed run must not stop the service; the next day (or restart) tries again.
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogFailed(logger, exception);
            return;
        }

        // One pass per tenant (ADR 0014); a failure in one is logged and does not stop the others.
        foreach (var tenantId in tenants)
        {
            try
            {
                var created = await tenantScope.RunAsync(
                    tenantId,
                    async (services, token) =>
                    {
                        var result = await services.GetRequiredService<NotifyDueSoonHandler>().HandleAsync(token);
                        return result.IsSuccess ? result.Value : 0;
                    },
                    cancellationToken);
                LogRan(logger, tenantId, created);
            }
#pragma warning disable CA1031
            catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
            {
                LogTenantFailed(logger, tenantId, exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Due-soon notifications created for tenant {TenantId}: {Count}")]
    private static partial void LogRan(ILogger logger, Guid tenantId, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "The due-soon notification job failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "The due-soon notification job failed for tenant {TenantId}")]
    private static partial void LogTenantFailed(ILogger logger, Guid tenantId, Exception exception);
}
