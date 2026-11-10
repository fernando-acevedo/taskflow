using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Abstractions;

/// <summary>
/// Runs the handlers registered for each domain event (ADR 0024). No mediator library: handlers are found by their interface in
/// the container, in registration order.
/// </summary>
public sealed partial class DomainEventDispatcher(IServiceProvider services, ILogger<DomainEventDispatcher> logger)
{
    private static readonly ConcurrentDictionary<Type, MethodInfo> Before = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo> After = new();

    private static readonly MethodInfo BeforeMethod = typeof(DomainEventDispatcher)
        .GetMethod(nameof(RunBeforeAsync), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly MethodInfo AfterMethod = typeof(DomainEventDispatcher)
        .GetMethod(nameof(RunAfterAsync), BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>Runs the before-commit handlers of the events in order. An exception from a handler propagates.</summary>
    public async Task BeforeCommitAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);
        foreach (var domainEvent in events)
        {
            var method = Before.GetOrAdd(domainEvent.GetType(), type => BeforeMethod.MakeGenericMethod(type));
            await (Task)method.Invoke(this, [domainEvent, cancellationToken])!;
        }
    }

    /// <summary>Runs the after-commit handlers of the events in order; a failing handler is logged and the rest still run.</summary>
    public async Task AfterCommitAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);
        var all = events.ToList();
        foreach (var domainEvent in all)
        {
            var method = After.GetOrAdd(domainEvent.GetType(), type => AfterMethod.MakeGenericMethod(type));
            await (Task)method.Invoke(this, [domainEvent, cancellationToken])!;
        }

        if (all.Count == 0)
        {
            return;
        }

        foreach (var handler in services.GetServices<IAfterCommitBatchHandler>())
        {
            try
            {
                await handler.HandleAsync(all, cancellationToken);
            }
#pragma warning disable CA1031 // After-commit handlers are best effort: nothing they throw may fail the request.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                LogHandlerFailed(exception, handler.GetType().Name, $"{all.Count} events");
            }
        }
    }

    private async Task RunBeforeAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        foreach (var handler in services.GetServices<IDomainEventHandler<TEvent>>())
        {
            await handler.HandleAsync(domainEvent, cancellationToken);
        }
    }

    private async Task RunAfterAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        foreach (var handler in services.GetServices<IAfterCommitHandler<TEvent>>())
        {
            try
            {
                await handler.HandleAsync(domainEvent, cancellationToken);
            }
#pragma warning disable CA1031 // After-commit handlers are best effort: nothing they throw may fail the request.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                LogHandlerFailed(exception, handler.GetType().Name, typeof(TEvent).Name);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "After-commit handler {Handler} failed for {Event}")]
    private partial void LogHandlerFailed(Exception exception, string handler, string @event);
}
