using System.Diagnostics.CodeAnalysis;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Abstractions;

/// <summary>
/// Reacts to a domain event <b>before</b> the commit, inside the same SaveChanges and transaction (ADR 0024): what it adds to
/// the context (activity, notifications, outbox rows) is saved or rolled back together with the change. Keep it quick and
/// database-only. A failure fails the request.
/// </summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Not a .NET event handler: a domain event handler (ADR 0024).")]
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}

/// <summary>
/// Reacts to a domain event <b>after</b> the change has been committed (ADR 0024), e.g. real-time publishing. Best effort: a
/// failure is logged and never fails the request.
/// </summary>
public interface IAfterCommitHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}

/// <summary>
/// Reacts <b>after</b> the commit to all the events of a save at once (ADR 0024), for work that is cheaper in bulk, such as reading
/// every changed task with one query. Same contract as <see cref="IAfterCommitHandler{TEvent}"/>: best effort, a failure is logged.
/// </summary>
public interface IAfterCommitBatchHandler
{
    Task HandleAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken);
}

/// <summary>
/// The id the client sent in <c>X-Client-Id</c> (a random id per browser tab), so that real-time events can say which client caused
/// them (ADR 0023). A hint, not an identity: <c>null</c> when absent or invalid.
/// </summary>
public interface IClientOrigin
{
    string? ClientId { get; }
}
