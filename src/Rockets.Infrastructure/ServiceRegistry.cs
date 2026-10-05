using Microsoft.Extensions.DependencyInjection;
using Rockets.Application.Messaging;
using Rockets.Domain.Rockets;
using Rockets.Infrastructure.Messaging;
using Rockets.Infrastructure.Rockets;

namespace Rockets.Infrastructure;

/// <summary>Registers this project's services, so its implementations can stay internal.</summary>
public static class ServiceRegistry
{
    /// <summary>
    /// Adds the in-memory rocket registry and the in-memory message channel. The caller still
    /// registers an <see cref="IRocketMonitorFactory"/> and binds <see cref="MessageChannelOptions"/>.
    /// </summary>
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddSingleton<IRocketRegistry, RocketRegistry>();
        services.AddSingleton<IMessageChannel, InMemoryMessageChannel>();

        return services;
    }
}
