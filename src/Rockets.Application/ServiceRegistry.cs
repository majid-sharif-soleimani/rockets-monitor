using Microsoft.Extensions.DependencyInjection;
using Rockets.Application.Consumers;
using Rockets.Application.Messaging;
using Rockets.Application.Queries;

namespace Rockets.Application;

/// <summary>Registers this project's services, so its implementations can stay internal.</summary>
public static class ServiceRegistry
{
    /// <summary>
    /// Adds the report services, the message consumer and the listener host. The caller still
    /// registers an <see cref="IMessageChannel"/>, the <see cref="IMessageListener"/>s and the
    /// rocket registry.
    /// </summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<IRocketQueryService, RocketQueryService>();
        services.AddSingleton<IFleetReportService, FleetReportService>();

        // Order matters. Hosted services stop in reverse order: the listeners stop accepting
        // messages first, then the consumer completes the channel and drains it.
        services.AddHostedService<RocketMessageConsumer>();
        services.AddHostedService<MessageListenersHostedService>();

        return services;
    }
}
