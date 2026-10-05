using Microsoft.Extensions.DependencyInjection;
using Rockets.Domain.Rockets;

namespace Rockets.Domain;

/// <summary>Registers this project's services, so its implementations can stay internal.</summary>
public static class ServiceRegistry
{
    /// <summary>Adds the factory that creates rocket monitors.</summary>
    public static IServiceCollection AddDomainServices(this IServiceCollection services)
    {
        services.AddSingleton<IRocketMonitorFactory, RocketMonitorFactory>();

        return services;
    }
}
