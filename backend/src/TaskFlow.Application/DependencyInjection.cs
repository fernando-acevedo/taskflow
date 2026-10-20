using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace TaskFlow.Application;

public static class DependencyInjection
{
    /// <summary>Registers every use-case handler (classes named *Handler) and every validator in this assembly.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var handlerTypes = typeof(DependencyInjection).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.Name.EndsWith("Handler", StringComparison.Ordinal));

        foreach (var handlerType in handlerTypes)
        {
            services.AddScoped(handlerType);
        }

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        return services;
    }
}
