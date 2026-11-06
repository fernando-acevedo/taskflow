using System.Data;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Common;

namespace TaskFlow.Infrastructure.Persistence;

/// <summary>Serializable transaction inside the SQL Server retrying execution strategy (a deadlock victim runs again).</summary>
internal sealed class TransactionRunner(AppDbContext db) : ITransactionRunner
{
    public Task<Result> RunSerializableAsync(Func<CancellationToken, Task<Result>> work, CancellationToken cancellationToken) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var result = await work(cancellationToken);
            if (result.IsSuccess)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return result;
        });
}
