using Microsoft.Extensions.Hosting;

namespace Rockets.Application.Messaging;

/// <summary>Starts and stops every registered <see cref="IMessageListener"/> with the host.</summary>
internal sealed class MessageListenersHostedService(IEnumerable<IMessageListener> listeners) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) =>
        Task.WhenAll(listeners.Select(l => l.StartAsync(cancellationToken)));

    public Task StopAsync(CancellationToken cancellationToken) =>
        Task.WhenAll(listeners.Select(l => l.StopAsync(cancellationToken)));
}
