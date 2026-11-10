using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Common;

namespace TaskFlow.Infrastructure.Persistence;

/// <summary>
/// Collects the domain events of the tracked entities when SaveChanges starts and runs their before-commit handlers, so what
/// they add is saved in the same SaveChanges and transaction. The after-commit handlers run once the save has succeeded; inside
/// an explicit transaction (<see cref="TransactionRunner"/>) they wait for the real commit (ADR 0024).
/// </summary>
public sealed partial class DomainEventInterceptor(DomainEventDispatcher dispatcher, ILogger<DomainEventInterceptor> logger) : SaveChangesInterceptor, IDisposable
{
    private const int MaxRounds = 10;

    private readonly List<IDomainEvent> _pending = [];

    // Events of a SaveChanges that failed: the entities no longer hold them, but their changes are still tracked, so a retried
    // SaveChanges on the same context saves them and must publish them too.
    private readonly List<IDomainEvent> _failed = [];

    // Index in _pending where the events of the SaveChanges in progress start.
    private int _saveStart;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        BeginSave();
        if (eventData.Context is { } context)
        {
            // A handler may raise events too (it changes entities), hence the rounds.
            for (var round = 0; round < MaxRounds; round++)
            {
                var events = context.ChangeTracker.Entries<IHasDomainEvents>()
                    .SelectMany(entry => entry.Entity.TakeDomainEvents())
                    .ToList();
                if (events.Count == 0)
                {
                    break;
                }

                Track(events);
                await dispatcher.BeforeCommitAsync(events, cancellationToken);
                context.ChangeTracker.DetectChanges();
            }
        }

        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context?.Database.CurrentTransaction is null)
        {
            await PublishAsync(cancellationToken);
        }

        return result;
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        FailSave();
        return Task.CompletedTask;
    }

    /// <summary>Marks where the events of a new SaveChanges start; the events of a failed one that is being retried come first.</summary>
    internal void BeginSave()
    {
        _saveStart = _pending.Count;
        if (_failed.Count > 0)
        {
            _pending.AddRange(_failed);
            _failed.Clear();
        }
    }

    internal void Track(IReadOnlyList<IDomainEvent> events) => _pending.AddRange(events);

    /// <summary>Drops only the events of the SaveChanges that failed (earlier saves of the same transaction stay), keeping them for a retry.</summary>
    internal void FailSave()
    {
        var start = Math.Min(_saveStart, _pending.Count);
        _failed.AddRange(_pending.Skip(start));
        _pending.RemoveRange(start, _pending.Count - start);
    }

    internal int PendingCount => _pending.Count;

    /// <summary>Runs the after-commit handlers of the events saved so far. Called once the transaction has really committed.</summary>
    internal async Task PublishAsync(CancellationToken cancellationToken)
    {
        if (_pending.Count == 0)
        {
            return;
        }

        var events = _pending.ToList();
        _pending.Clear();
        await dispatcher.AfterCommitAsync(events, cancellationToken);
    }

    /// <summary>Forgets the events of a transaction that was rolled back or is about to run again.</summary>
    internal void Discard()
    {
        _pending.Clear();
        _failed.Clear();
        _saveStart = 0;
    }

    /// <summary>The scope ends: events still pending belong to a transaction that was never committed through the runner (ADR 0024).</summary>
    public void Dispose()
    {
        if (_pending.Count > 0)
        {
            LogUnpublished(_pending.Count);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Count} domain event(s) were never published: a transaction was left open outside ITransactionRunner")]
    private partial void LogUnpublished(int count);
}
