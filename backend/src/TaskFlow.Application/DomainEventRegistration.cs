using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaskFlow.Application.Abstractions;

namespace TaskFlow.Application;

public static class DomainEventRegistration
{
    /// <summary>
    /// Registers, by convention, every class of the assembly that implements <see cref="IDomainEventHandler{TEvent}"/> or
    /// <see cref="IAfterCommitHandler{TEvent}"/> (ADR 0024), once per event it handles.
    /// </summary>
    public static IServiceCollection AddDomainEventHandlers(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assembly);
        foreach (var type in assembly.GetTypes().Where(type => type is { IsClass: true, IsAbstract: false }).OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            foreach (var contract in type.GetInterfaces().Where(IsEventHandler))
            {
                services.TryAddEnumerable(ServiceDescriptor.Scoped(contract, type));
            }
        }

        foreach (var type in assembly.GetTypes().Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IAfterCommitBatchHandler).IsAssignableFrom(type)))
        {
            services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IAfterCommitBatchHandler), type));
        }

        services.TryAddScoped<DomainEventDispatcher>();
        return services;
    }

    private static bool IsEventHandler(Type contract) =>
        contract.IsGenericType
        && (contract.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>) || contract.GetGenericTypeDefinition() == typeof(IAfterCommitHandler<>));
}
