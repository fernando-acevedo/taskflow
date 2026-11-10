using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Activity;
using TaskFlow.Application.Notifications;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.UnitTests;

/// <summary>
/// Stands in for the SaveChanges interceptor in unit tests with a mocked context: takes the events the entities raised and runs the
/// real before-commit handlers, so a test can look at the activity and notifications a use case ends up with.
/// </summary>
internal static class EventPipeline
{
    public static async Task BeforeCommitAsync(IAppDbContext db, ICurrentUser user, params IHasDomainEvents[] entities)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton(user);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Substitute.For<IIdentityService>());
        services.AddSingleton(Substitute.For<INotificationEmailSender>());
        services.AddSingleton<ILogger<DomainEventDispatcher>>(NullLogger<DomainEventDispatcher>.Instance);
        services.AddScoped<ActivityLog>();
        services.AddScoped<Notifier>();
        services.AddDomainEventHandlers(typeof(DomainEventRegistration).Assembly);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var events = entities.SelectMany(entity => entity.TakeDomainEvents()).ToList();
        await scope.ServiceProvider.GetRequiredService<DomainEventDispatcher>().BeforeCommitAsync(events, TestContext.Current.CancellationToken);
    }
}
