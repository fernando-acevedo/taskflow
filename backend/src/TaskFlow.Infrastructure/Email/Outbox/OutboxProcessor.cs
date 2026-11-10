using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskFlow.Infrastructure.Caching;
using TaskFlow.Infrastructure.Diagnostics;

namespace TaskFlow.Infrastructure.Email.Outbox;

/// <summary>Polls the outbox every <see cref="OutboxOptions.PollInterval"/> and sends what is due (ADR 0018).</summary>
internal sealed partial class OutboxProcessor(
    OutboxDispatcher dispatcher,
    JobLock jobLock,
    IOptions<OutboxOptions> options,
    TimeProvider time,
    ILogger<OutboxProcessor> logger) : BackgroundService
{
    private static readonly TimeSpan CleanUpEvery = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        var interval = options.Value.PollInterval > TimeSpan.Zero ? options.Value.PollInterval : TimeSpan.FromSeconds(5);
        var lastCleanUp = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using (TaskFlowTelemetry.Source.StartActivity("outbox.process"))
                {
                    await dispatcher.ProcessPendingAsync(stoppingToken);
                }

                if (time.GetUtcNow() - lastCleanUp >= CleanUpEvery)
                {
                    lastCleanUp = time.GetUtcNow();
                    await CleanUpAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // A failed poll (the database is down, say) must not stop the service; the next one tries again.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                LogPollFailed(logger, exception);
            }

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

    // Claiming is already safe between instances; the clean-up only needs one of them at a time (ADR 0020).
    private async Task CleanUpAsync(CancellationToken cancellationToken)
    {
        using var activity = TaskFlowTelemetry.Source.StartActivity("outbox.cleanup");
        var held = await jobLock.TryAcquireAsync("taskflow-outbox-cleanup", cancellationToken);
        if (held is null)
        {
            activity?.SetTag("job.skipped", true);
            return;
        }

        await using (held)
        {
            await dispatcher.CleanUpAsync(cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The outbox poll failed")]
    private static partial void LogPollFailed(ILogger logger, Exception exception);
}
