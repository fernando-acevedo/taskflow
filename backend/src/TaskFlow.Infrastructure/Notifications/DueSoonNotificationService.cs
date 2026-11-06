using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskFlow.Application.Notifications;

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
/// Runs <see cref="NotifyDueSoonHandler"/> once a day at <see cref="NotificationOptions.DueSoonHourUtc"/>. If the
/// app starts after that hour it runs at once; the handler is idempotent, so a restart or a second instance
/// doesn't duplicate notifications (ADR 0010).
/// </summary>
internal sealed partial class DueSoonNotificationService(
    IServiceScopeFactory scopes,
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
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<NotifyDueSoonHandler>().HandleAsync(cancellationToken);
            LogRan(logger, result.IsSuccess ? result.Value : 0);
        }
#pragma warning disable CA1031 // A failed run must not stop the service; the next day (or restart) tries again.
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogFailed(logger, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Due-soon notifications created: {Count}")]
    private static partial void LogRan(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "The due-soon notification job failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
