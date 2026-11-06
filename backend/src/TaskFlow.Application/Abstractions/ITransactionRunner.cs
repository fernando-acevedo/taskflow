using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Abstractions;

/// <summary>
/// Runs a check-then-write use case atomically in a Serializable transaction (for rules such as "the tenant keeps
/// at least one Owner"). The work is committed when it returns a success and rolled back otherwise. It may run more
/// than once (a deadlock between two concurrent requests is retried), so it must load everything it needs inside.
/// </summary>
public interface ITransactionRunner
{
    Task<Result> RunSerializableAsync(Func<CancellationToken, Task<Result>> work, CancellationToken cancellationToken);
}
