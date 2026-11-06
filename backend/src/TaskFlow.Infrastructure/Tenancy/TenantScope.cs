using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Infrastructure.Tenancy;

/// <summary>
/// Runs work that has no request (background services, seeding, tests) inside one tenant (ADR 0014). It opens a
/// DI scope, fixes the tenant on that scope's <see cref="TenantContext"/> the way the tenant middleware does for a
/// request, and runs the work with the scope's services. The scope, and with it the tenant, ends with the work.
/// It is the only sanctioned way to touch tenant data outside a request.
/// </summary>
public sealed class TenantScope(IServiceScopeFactory scopes)
{
    public async Task RunAsync(Guid tenantId, Func<IServiceProvider, CancellationToken, Task> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        await RunAsync<object?>(
            tenantId,
            async (services, token) =>
            {
                await work(services, token);
                return null;
            },
            cancellationToken);
    }

    public async Task<T> RunAsync<T>(Guid tenantId, Func<IServiceProvider, CancellationToken, Task<T>> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Use(tenantId);
        return await work(scope.ServiceProvider, cancellationToken);
    }
}
