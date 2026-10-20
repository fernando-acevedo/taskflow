using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaskFlow.Application.Projects;

namespace TaskFlow.Application;

public static class DependencyInjection
{
    /// <summary>Registers every use-case handler (classes named *Handler), every validator in this assembly, and their shared services.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var handlerTypes = typeof(DependencyInjection).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.Name.EndsWith("Handler", StringComparison.Ordinal));

        foreach (var handlerType in handlerTypes)
        {
            services.AddScoped(handlerType);
        }

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        services.AddScoped<ProjectAccess>();
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }
}
