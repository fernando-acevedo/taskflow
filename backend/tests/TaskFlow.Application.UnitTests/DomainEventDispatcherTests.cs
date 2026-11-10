using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.UnitTests;

public sealed class DomainEventDispatcherTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static (DomainEventDispatcher Dispatcher, Log Log) Build()
    {
        var services = new ServiceCollection();
        var log = new Log();
        services.AddSingleton(log);
        services.AddLogging();
        services.AddScoped<IDomainEventHandler<Ping>, FirstBefore>();
        services.AddScoped<IDomainEventHandler<Ping>, SecondBefore>();
        services.AddScoped<IDomainEventHandler<Pong>, FailingBefore>();
        services.AddScoped<IAfterCommitHandler<Ping>, FailingAfter>();
        services.AddScoped<IAfterCommitHandler<Ping>, WorkingAfter>();
        services.AddScoped<DomainEventDispatcher>();
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<DomainEventDispatcher>(), log);
    }

    [Fact]
    public async Task BeforeCommitRunsEveryHandlerOfEveryEventInOrderAndOnlyThoseHandlers()
    {
        var (dispatcher, log) = Build();

        await dispatcher.BeforeCommitAsync([new Ping(1), new Ping(2)], Ct);

        log.Lines.ShouldBe(["before-1:1", "before-2:1", "before-1:2", "before-2:2"]);
    }

    [Fact]
    public async Task ABeforeCommitFailurePropagatesSoTheSaveFails()
    {
        var (dispatcher, _) = Build();

        await Should.ThrowAsync<InvalidOperationException>(() => dispatcher.BeforeCommitAsync([new Pong()], Ct));
    }

    [Fact]
    public async Task AfterCommitIsBestEffortAndSkipsTheBeforeHandlers()
    {
        var (dispatcher, log) = Build();

        await dispatcher.AfterCommitAsync([new Ping(7)], Ct);

        log.Lines.ShouldBe(["after:7"]);
    }

    [Fact]
    public async Task BatchHandlersGetAllTheEventsOnceAndAFailingOneDoesNotStopTheOthers()
    {
        var log = new Log();
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddLogging();
        services.AddScoped<IAfterCommitBatchHandler, FailingBatch>();
        services.AddScoped<IAfterCommitBatchHandler, WorkingBatch>();
        services.AddScoped<DomainEventDispatcher>();
        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<DomainEventDispatcher>().AfterCommitAsync([new Ping(1), new Pong(), new Ping(2)], Ct);
        await provider.GetRequiredService<DomainEventDispatcher>().AfterCommitAsync([], Ct);

        log.Lines.ShouldBe(["batch:3"]);
    }

    [Fact]
    public async Task AnEventWithoutHandlersIsIgnored()
    {
        var (dispatcher, log) = Build();

        await dispatcher.AfterCommitAsync([new Pong()], Ct);

        log.Lines.ShouldBeEmpty();
    }

    [Fact]
    public void ConventionRegistrationFindsTheHandlersOfAnAssembly()
    {
        var services = new ServiceCollection().AddDomainEventHandlers(typeof(DomainEventRegistration).Assembly);

        services.Count(descriptor => descriptor.ServiceType.IsGenericType
            && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>)).ShouldBeGreaterThan(5);
    }

    private sealed record Ping(int Number) : IDomainEvent;

    private sealed record Pong : IDomainEvent;

    private sealed class Log
    {
        public List<string> Lines { get; } = [];
    }

    private sealed class FirstBefore(Log log) : IDomainEventHandler<Ping>
    {
        public Task HandleAsync(Ping domainEvent, CancellationToken cancellationToken)
        {
            log.Lines.Add($"before-1:{domainEvent.Number}");
            return Task.CompletedTask;
        }
    }

    private sealed class SecondBefore(Log log) : IDomainEventHandler<Ping>
    {
        public Task HandleAsync(Ping domainEvent, CancellationToken cancellationToken)
        {
            log.Lines.Add($"before-2:{domainEvent.Number}");
            return Task.CompletedTask;
        }
    }

    private sealed class FailingBefore : IDomainEventHandler<Pong>
    {
        public Task HandleAsync(Pong domainEvent, CancellationToken cancellationToken) => throw new InvalidOperationException("boom");
    }

    private sealed class FailingAfter : IAfterCommitHandler<Ping>
    {
        public Task HandleAsync(Ping domainEvent, CancellationToken cancellationToken) => throw new InvalidOperationException("boom");
    }

    private sealed class FailingBatch : IAfterCommitBatchHandler
    {
        public Task HandleAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken) => throw new InvalidOperationException("boom");
    }

    private sealed class WorkingBatch(Log log) : IAfterCommitBatchHandler
    {
        public Task HandleAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken)
        {
            log.Lines.Add($"batch:{events.Count}");
            return Task.CompletedTask;
        }
    }

    private sealed class WorkingAfter(Log log) : IAfterCommitHandler<Ping>
    {
        public Task HandleAsync(Ping domainEvent, CancellationToken cancellationToken)
        {
            log.Lines.Add($"after:{domainEvent.Number}");
            return Task.CompletedTask;
        }
    }
}
