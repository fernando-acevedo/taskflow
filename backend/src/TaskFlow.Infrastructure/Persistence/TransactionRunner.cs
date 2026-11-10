using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Common;

namespace TaskFlow.Infrastructure.Persistence;

/// <summary>Serializable transaction (after-commit domain event handlers run once it has committed, ADR 0024) inside the SQL Server retrying execution strategy (a deadlock victim runs again).</summary>
internal sealed partial class TransactionRunner(AppDbContext db, DomainEventInterceptor events, ILogger<TransactionRunner> logger) : ITransactionRunner
{
    public Task<Result> RunSerializableAsync(Func<CancellationToken, Task<Result>> work, CancellationToken cancellationToken) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            events.Discard();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var result = await work(cancellationToken);
            if (!result.IsSuccess)
            {
                events.Discard();
                return result;
            }

            await transaction.CommitAsync(cancellationToken);

            // Only now has the change really happened: a retry or a rollback above never reaches the after-commit handlers.
            try
            {
                await events.PublishAsync(cancellationToken);
            }
#pragma warning disable CA1031 // The change is committed: publishing is best effort and must not turn it into a failed request.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                LogPublishFailed(exception);
            }

            return result;
        });

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publishing the after-commit events of a committed transaction failed")]
    private partial void LogPublishFailed(Exception exception);
}
