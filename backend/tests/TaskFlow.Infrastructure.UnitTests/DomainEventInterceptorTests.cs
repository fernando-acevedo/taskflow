using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Common;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Infrastructure.UnitTests;

/// <summary>What the interceptor keeps pending across several SaveChanges of one transaction, and when one of them fails (ADR 0024).</summary>
public sealed class DomainEventInterceptorTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private sealed record Ping(int Id) : IDomainEvent;

    private sealed class RecordingHandler(List<int> seen) : IAfterCommitHandler<Ping>
    {
        public Task HandleAsync(Ping domainEvent, CancellationToken cancellationToken)
        {
            seen.Add(domainEvent.Id);
            return Task.CompletedTask;
        }
    }

    private static (DomainEventInterceptor Interceptor, List<int> Seen, ServiceProvider Provider) Build(ILogger<DomainEventInterceptor>? logger = null)
    {
        var seen = new List<int>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IAfterCommitHandler<Ping>>(new RecordingHandler(seen));
        services.AddScoped<DomainEventDispatcher>();
        var provider = services.BuildServiceProvider();
        var interceptor = new DomainEventInterceptor(provider.GetRequiredService<DomainEventDispatcher>(), logger ?? NullLogger<DomainEventInterceptor>.Instance);
        return (interceptor, seen, provider);
    }

    private static void Save(DomainEventInterceptor interceptor, params int[] ids)
    {
        interceptor.BeginSave();
        interceptor.Track([.. ids.Select(id => new Ping(id))]);
    }

    [Fact]
    public async Task AHandledFailureKeepsTheEarlierEventsAndPublishesThemOnCommit()
    {
        var (interceptor, seen, provider) = Build();
        await using var _ = provider;
        Save(interceptor, 1, 2);
        Save(interceptor, 3);
        interceptor.FailSave();

        interceptor.PendingCount.ShouldBe(2);
        await interceptor.PublishAsync(Ct);
        seen.ShouldBe([1, 2]);
    }

    [Fact]
    public async Task ARetriedSaveHasItsEventsPublishedOnceNotLostNorDuplicated()
    {
        var (interceptor, seen, provider) = Build();
        await using var _ = provider;
        Save(interceptor, 1);
        Save(interceptor, 2, 3);
        interceptor.FailSave();

        // The retry finds no events on the entities (they were taken), but the failed ones come back.
        interceptor.BeginSave();
        interceptor.PendingCount.ShouldBe(3);
        await interceptor.PublishAsync(Ct);
        seen.ShouldBe([1, 2, 3]);

        await interceptor.PublishAsync(Ct);
        seen.Count.ShouldBe(3);
    }

    [Fact]
    public async Task ARetriedSaveThatFailsAgainStillOnlyDropsItsOwnEvents()
    {
        var (interceptor, seen, provider) = Build();
        await using var _ = provider;
        Save(interceptor, 1);
        Save(interceptor, 2);
        interceptor.FailSave();
        interceptor.BeginSave();
        interceptor.FailSave();
        interceptor.BeginSave();

        await interceptor.PublishAsync(Ct);
        seen.ShouldBe([1, 2]);
    }

    [Fact]
    public async Task DiscardForgetsEverythingIncludingFailedEvents()
    {
        var (interceptor, seen, provider) = Build();
        await using var _ = provider;
        Save(interceptor, 1);
        interceptor.FailSave();
        interceptor.Discard();
        interceptor.BeginSave();

        await interceptor.PublishAsync(Ct);
        seen.ShouldBeEmpty();
    }

    [Fact]
    public async Task DisposingWithPendingEventsLogsAWarning()
    {
        var logger = new CapturingLogger();
        var (interceptor, _, provider) = Build(logger);
        await using var _ = provider;
        Save(interceptor, 1);

        interceptor.Dispose();

        logger.Warnings.ShouldHaveSingleItem().ShouldContain("never published");
    }

    [Fact]
    public async Task DisposingWithNothingPendingStaysQuiet()
    {
        var logger = new CapturingLogger();
        var (interceptor, _, provider) = Build(logger);
        await using var _ = provider;
        Save(interceptor, 1);
        await interceptor.PublishAsync(Ct);

        interceptor.Dispose();

        logger.Warnings.ShouldBeEmpty();
    }

    private sealed class CapturingLogger : ILogger<DomainEventInterceptor>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}
