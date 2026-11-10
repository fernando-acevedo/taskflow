using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskFlow.Infrastructure.Diagnostics;

namespace TaskFlow.Infrastructure.Demo;

/// <summary>
/// Checks at startup and every <see cref="DemoOptions.CheckIntervalMinutes"/> whether the public demo is due for a
/// reset (ADR 0005). The reset takes its own application lock and reads the persisted reset log inside it, so
/// several instances never reset twice. The app may sleep through the scheduled time, so the check compares the last reset with the
/// latest scheduled occurrence instead of waiting for a timer to fire.
/// </summary>
internal sealed partial class DemoResetService(
    IServiceScopeFactory scopes,
    IOptions<DemoOptions> options,
    TimeProvider time,
    ILogger<DemoResetService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, settings.CheckIntervalMinutes));
        while (!stoppingToken.IsCancellationRequested)
        {
            await CheckAsync(stoppingToken);
            try
            {
                await Task.Delay(interval, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var activity = TaskFlowTelemetry.Source.StartActivity("demo-reset.check");
            await using var scope = scopes.CreateAsyncScope();
            if (await scope.ServiceProvider.GetRequiredService<DemoResetter>().ResetIfDueAsync(cancellationToken))
            {
                LogReset(logger);
            }
        }
#pragma warning disable CA1031 // A failed reset must not stop the service; the next check tries again.
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogFailed(logger, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The public demo was reset")]
    private static partial void LogReset(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "The public demo reset failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
