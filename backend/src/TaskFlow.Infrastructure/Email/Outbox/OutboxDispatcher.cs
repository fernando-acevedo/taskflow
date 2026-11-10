using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Infrastructure.Email.Outbox;

/// <summary>
/// Sends the due outbox messages through <see cref="IEmailSender"/> (ADR 0018). Safe with several workers: a batch is
/// claimed with one <c>UPDATE</c> over rows locked with <c>UPDLOCK, READPAST</c> (a row another worker is claiming is
/// skipped) that also sets a lease, so two workers never send the same row, and a crashed worker's rows come back
/// when the lease expires. Delivery is at least once.
/// </summary>
public sealed partial class OutboxDispatcher(
    IServiceScopeFactory scopes,
    IEmailSender sender,
    IOptions<OutboxOptions> options,
    TimeProvider time,
    ILogger<OutboxDispatcher> logger)
{
    /// <summary>What the payload becomes once it is no longer needed (the column is required).</summary>
    public const string EmptyPayload = "{}";

    /// <summary>Sends every message that is due now, batch after batch.</summary>
    /// <returns>How many were sent.</returns>
    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        var sent = 0;
        int claimed;
        do
        {
            int batchSent;
            (claimed, batchSent) = await ProcessBatchAsync(cancellationToken);
            sent += batchSent;
        }
        while (claimed > 0);

        return sent;
    }

    /// <summary>
    /// Deletes the messages processed more than <see cref="OutboxOptions.RetentionDays"/> days ago and the ones left as
    /// failed more than <see cref="OutboxOptions.FailedRetentionDays"/> days ago.
    /// </summary>
    public async Task<int> CleanUpAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = time.GetUtcNow();
        var processedLimit = now.AddDays(-options.Value.RetentionDays);
        var failedLimit = now.AddDays(-options.Value.FailedRetentionDays);
        return await db.OutboxMessages
            .Where(message => (message.ProcessedAt != null && message.ProcessedAt < processedLimit)
                || (message.FailedAt != null && message.FailedAt < failedLimit))
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task<(int Claimed, int Sent)> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = time.GetUtcNow();
        var token = Guid.NewGuid();
        var until = now + settings.LeaseDuration;
        var batch = Math.Max(1, settings.BatchSize);
        // READPAST needs READ COMMITTED or REPEATABLE READ, and a pooled connection can keep the SERIALIZABLE level of an
        // earlier transaction (SqlClient does not reset it), so the claim sets its own.
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            WITH due AS (
                SELECT TOP ({batch}) * FROM OutboxMessages WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE ProcessedAt IS NULL AND FailedAt IS NULL AND NextAttemptAt <= {now}
                    AND (LockedUntil IS NULL OR LockedUntil <= {now})
                ORDER BY NextAttemptAt)
            UPDATE due SET LockToken = {token}, LockedUntil = {until}, Attempts = Attempts + 1
            """,
            cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });

        var messages = await db.OutboxMessages.AsNoTracking()
            .Where(message => message.LockToken == token)
            .OrderBy(message => message.NextAttemptAt)
            .ToListAsync(cancellationToken);

        var sent = 0;
        foreach (var message in messages)
        {
            if (await SendAsync(db, message, token, cancellationToken))
            {
                sent++;
            }
        }

        return (messages.Count, sent);
    }

    private async Task<bool> SendAsync(AppDbContext db, OutboxMessage message, Guid token, CancellationToken cancellationToken)
    {
        var claimed = db.OutboxMessages.Where(row => row.Id == message.Id && row.LockToken == token);
        try
        {
            var email = JsonSerializer.Deserialize<EmailMessage>(message.Payload)
                ?? throw new InvalidOperationException("The payload is empty.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.Value.SendTimeout);
            try
            {
                await sender.SendAsync(email, timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"The send took longer than {options.Value.SendTimeout}.");
            }
        }
#pragma warning disable CA1031 // Any failure (settings, network, SMTP, a bad payload, a timeout) schedules a retry; it must not stop the batch.
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            await RecordFailureAsync(claimed, message, exception, cancellationToken);
            return false;
        }

        var now = time.GetUtcNow();
        await claimed.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(row => row.ProcessedAt, now)
                .SetProperty(row => row.Payload, EmptyPayload)
                .SetProperty(row => row.LastError, (string?)null)
                .SetProperty(row => row.LockedUntil, (DateTimeOffset?)null)
                .SetProperty(row => row.LockToken, (Guid?)null),
            cancellationToken);
        return true;
    }

    private async Task RecordFailureAsync(
        IQueryable<OutboxMessage> claimed, OutboxMessage message, Exception exception, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = time.GetUtcNow();
        var error = Describe(exception);
        var failed = message.Attempts >= settings.MaxAttempts;
        var next = now + Backoff(message.Attempts, settings);
        if (failed)
        {
            LogGaveUp(logger, message.Id, message.Kind, message.Attempts, error);
        }
        else
        {
            LogRetry(logger, message.Id, message.Kind, message.Attempts, next, error);
        }

        // A message given up on keeps only its error: the payload holds reset and invitation links, which must not stay at rest.
        DateTimeOffset? failedAt = failed ? now : null;
        await claimed.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(row => row.LastError, error)
                .SetProperty(row => row.FailedAt, failedAt)
                .SetProperty(row => row.Payload, row => failed ? EmptyPayload : row.Payload)
                .SetProperty(row => row.NextAttemptAt, next)
                .SetProperty(row => row.LockedUntil, (DateTimeOffset?)null)
                .SetProperty(row => row.LockToken, (Guid?)null),
            cancellationToken);
    }

    /// <summary>Doubles from <see cref="OutboxOptions.BaseDelay"/> up to <see cref="OutboxOptions.MaxDelay"/>, with up to 20% jitter either way.</summary>
    internal static TimeSpan Backoff(int attempts, OutboxOptions settings)
    {
        var exponent = Math.Min(Math.Max(attempts - 1, 0), 20);
        var delay = Math.Min(settings.BaseDelay.TotalSeconds * Math.Pow(2, exponent), settings.MaxDelay.TotalSeconds);
        var jitter = 1 + ((Random.Shared.NextDouble() * 0.4) - 0.2);
        return TimeSpan.FromSeconds(delay * jitter);
    }

    // Type and message only: the message never carries the email's content, and is cut so a long one can't fill the table.
    private static string Describe(Exception exception)
    {
        var text = $"{exception.GetType().Name}: {exception.Message}";
        return text.Length <= OutboxMessage.LastErrorMaxLength ? text : text[..OutboxMessage.LastErrorMaxLength];
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox message {Id} ({Kind}) failed on attempt {Attempts}; next attempt at {NextAttempt}: {Error}")]
    private static partial void LogRetry(ILogger logger, Guid id, string kind, int attempts, DateTimeOffset nextAttempt, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox message {Id} ({Kind}) failed after {Attempts} attempts and was left as failed: {Error}")]
    private static partial void LogGaveUp(ILogger logger, Guid id, string kind, int attempts, string error);
}
