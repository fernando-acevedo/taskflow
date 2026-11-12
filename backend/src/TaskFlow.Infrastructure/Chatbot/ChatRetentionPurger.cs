using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskFlow.Infrastructure.Caching;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Infrastructure.Chatbot;

/// <summary>Deletes chat sessions idle for longer than the retention; messages and intent logs go with them by cascade.</summary>
public sealed class ChatRetentionPurger(IServiceScopeFactory scopes, IOptions<ChatRetentionOptions> options, TimeProvider time)
{
    public async Task<int> PurgeAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var limit = time.GetUtcNow().AddDays(-settings.Days);
        var batch = Math.Max(1, settings.BatchSize);
        var total = 0;
        while (true)
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ids = await db.ChatSessions
                .Where(session => session.LastActivityAt < limit)
                .OrderBy(session => session.LastActivityAt)
                .Select(session => session.Id)
                .Take(batch)
                .ToListAsync(cancellationToken);
            if (ids.Count == 0)
            {
                return total;
            }

            total += await db.ChatSessions.Where(session => ids.Contains(session.Id)).ExecuteDeleteAsync(cancellationToken);
            if (ids.Count < batch)
            {
                return total;
            }
        }
    }
}

/// <summary>Runs <see cref="ChatRetentionPurger"/> every <see cref="ChatRetentionOptions.Interval"/>, one instance at a time (ADR 0020, 0026).</summary>
internal sealed partial class ChatRetentionWorker(
    ChatRetentionPurger purger,
    JobLock jobLock,
    IOptions<ChatRetentionOptions> options,
    TimeProvider time,
    ILogger<ChatRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var held = await jobLock.TryAcquireAsync("taskflow-chat-retention", stoppingToken);
                if (held is not null)
                {
                    await using (held)
                    {
                        var deleted = await purger.PurgeAsync(stoppingToken);
                        if (deleted > 0)
                        {
                            LogPurged(logger, deleted);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // A failed purge (the database is down, say) must not stop the service; the next one tries again.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                LogFailed(logger, exception);
            }

            try
            {
                await Task.Delay(options.Value.Interval, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {Count} chat sessions past their retention")]
    private static partial void LogPurged(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "The chat retention purge failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
