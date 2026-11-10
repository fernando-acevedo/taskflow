using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Api.IntegrationTests.Infrastructure;
using TaskFlow.Infrastructure.Caching;

namespace TaskFlow.Api.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class JobLockTests(SqlServerApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OnlyOneOfTwoConcurrentAcquisitionsGetsTheLock()
    {
        var jobLock = factory.Services.GetRequiredService<JobLock>();
        var resource = $"test-{Guid.NewGuid():N}";

        var first = await jobLock.TryAcquireAsync(resource, Ct);
        var second = await jobLock.TryAcquireAsync(resource, Ct);

        first.ShouldNotBeNull();
        second.ShouldBeNull();

        // Another resource is independent, and once released the lock can be taken again.
        await using (var other = await jobLock.TryAcquireAsync($"{resource}-other", Ct))
        {
            other.ShouldNotBeNull();
        }

        await first.DisposeAsync();
        await using var third = await jobLock.TryAcquireAsync(resource, Ct);
        third.ShouldNotBeNull();
    }

    [Fact]
    public async Task ManyInstancesRacingForTheLockLetExactlyOneRun()
    {
        var jobLock = factory.Services.GetRequiredService<JobLock>();
        var resource = $"test-{Guid.NewGuid():N}";
        var running = 0;
        var maxRunning = 0;
        var ran = 0;

        await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            await using var held = await jobLock.TryAcquireAsync(resource, Ct);
            if (held is null)
            {
                return;
            }

            var now = Interlocked.Increment(ref running);
            maxRunning = Math.Max(maxRunning, now);
            Interlocked.Increment(ref ran);
            await Task.Delay(300, Ct);
            Interlocked.Decrement(ref running);
        }));

        maxRunning.ShouldBe(1);
        ran.ShouldBeGreaterThanOrEqualTo(1);
    }
}
