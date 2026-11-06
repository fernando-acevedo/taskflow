using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Application.Abstractions;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.Infrastructure.Tenancy;

namespace TaskFlow.Infrastructure.UnitTests;

public sealed class TenantScopeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(provider => provider.GetRequiredService<TenantContext>());
        services.AddSingleton<TenantScope>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task TheWorkSeesTheTenantAndNothingLeaksOutside()
    {
        await using var provider = Services();
        var tenantId = Guid.NewGuid();

        var seen = await provider.GetRequiredService<TenantScope>().RunAsync(
            tenantId,
            (services, _) => Task.FromResult(services.GetRequiredService<ITenantContext>().TenantId),
            Ct);

        seen.ShouldBe(tenantId);
        await using var other = provider.CreateAsyncScope();
        other.ServiceProvider.GetRequiredService<ITenantContext>().TenantId.ShouldBeNull();
    }

    [Fact]
    public async Task EachRunGetsItsOwnTenant()
    {
        await using var provider = Services();
        var scope = provider.GetRequiredService<TenantScope>();
        var (first, second) = (Guid.NewGuid(), Guid.NewGuid());

        var seenFirst = await scope.RunAsync(first, (services, _) => Task.FromResult(services.GetRequiredService<ITenantContext>().TenantId), Ct);
        var seenSecond = await scope.RunAsync(second, (services, _) => Task.FromResult(services.GetRequiredService<ITenantContext>().TenantId), Ct);

        seenFirst.ShouldBe(first);
        seenSecond.ShouldBe(second);
    }
}
