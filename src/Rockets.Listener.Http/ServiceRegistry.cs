using Microsoft.Extensions.DependencyInjection;
using Rockets.Application.Messaging;

namespace Rockets.Listener.Http;

/// <summary>Registers this project's services, so its implementations can stay internal.</summary>
public static class ServiceRegistry
{
    /// <summary>
    /// Adds the HTTP message listener. The caller still registers an <see cref="IMessageChannel"/>
    /// and binds <see cref="HttpListenerOptions"/> and <see cref="RateLimitOptions"/>.
    /// </summary>
    public static IServiceCollection AddHttpMessageListener(this IServiceCollection services)
    {
        services.AddSingleton<IMessageListener, HttpMessageListener>();

        return services;
    }
}
