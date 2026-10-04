using Rockets.Domain.Messages;

namespace Rockets.Application.Messaging;

/// <summary>
/// The queue between listeners (producers) and the consumer. The default implementation is
/// in-memory; a durable one (e.g. Kafka) can replace it.
/// </summary>
public interface IMessageChannel
{
    /// <summary>Number of messages waiting to be consumed.</summary>
    int Count { get; }

    /// <summary>
    /// Writes a message. Completes only once the message is in the channel.
    /// </summary>
    /// <exception cref="MessageChannelFullException">The channel stayed full for too long.</exception>
    /// <exception cref="MessageChannelUnavailableException">The channel no longer accepts messages.</exception>
    ValueTask WriteAsync(RocketMessage message, CancellationToken cancellationToken);

    /// <summary>Reads messages until the channel is completed and drained, or cancellation.</summary>
    IAsyncEnumerable<RocketMessage> ReadAllAsync(CancellationToken cancellationToken);

    /// <summary>Stops accepting messages. Messages already written can still be read.</summary>
    void Complete();
}

public class MessageChannelUnavailableException(string message) : Exception(message);

public sealed class MessageChannelFullException(string message) : MessageChannelUnavailableException(message);
