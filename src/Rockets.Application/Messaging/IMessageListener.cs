namespace Rockets.Application.Messaging;

/// <summary>
/// Receives rocket messages from a transport and writes them to the <see cref="IMessageChannel"/>.
/// The default implementation is an HTTP server; other transports can be added alongside it.
/// </summary>
public interface IMessageListener
{
    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}
