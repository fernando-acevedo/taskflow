using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TaskFlow.Infrastructure.Email.Outbox;

/// <summary>Polls the outbox every <see cref="OutboxOptions.PollInterval"/> and sends what is due (ADR 0018).</summary>
internal sealed partial class OutboxProcessor(
    OutboxDispatcher dispatcher,
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
                await dispatcher.ProcessPendingAsync(stoppingToken);
                if (time.GetUtcNow() - lastCleanUp >= CleanUpEvery)
                {
                    lastCleanUp = time.GetUtcNow();
                    await dispatcher.CleanUpAsync(stoppingToken);
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

    [LoggerMessage(Level = LogLevel.Error, Message = "The outbox poll failed")]
    private static partial void LogPollFailed(ILogger logger, Exception exception);
}
