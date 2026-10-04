using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Rockets.Application.Messaging;
using Rockets.Domain.Messages;

namespace Rockets.Infrastructure.Messaging;

/// <summary>
/// Bounded in-memory channel. <see cref="BoundedChannelFullMode.Wait"/> is used so that messages
/// which were already acknowledged are never dropped; a write that cannot find space within
/// <see cref="MessageChannelOptions.WriteTimeout"/> fails instead, so the sender redelivers it.
/// </summary>
public sealed class InMemoryMessageChannel : IMessageChannel
{
    private readonly Channel<RocketMessage> _channel;
    private readonly TimeSpan _writeTimeout;

    public InMemoryMessageChannel(IOptions<MessageChannelOptions> options)
    {
        var settings = options.Value;
        _writeTimeout = settings.WriteTimeout;
        _channel = Channel.CreateBounded<RocketMessage>(new BoundedChannelOptions(settings.Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public int Count => _channel.Reader.Count;

    public async ValueTask WriteAsync(RocketMessage message, CancellationToken cancellationToken)
    {
        if (_channel.Writer.TryWrite(message))
        {
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_writeTimeout);
        try
        {
            while (await _channel.Writer.WaitToWriteAsync(timeout.Token))
            {
                if (_channel.Writer.TryWrite(message))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MessageChannelFullException(
                $"The message channel is full (capacity reached for {_writeTimeout.TotalMilliseconds} ms).");
        }

        throw new MessageChannelUnavailableException("The message channel no longer accepts messages.");
    }

    public IAsyncEnumerable<RocketMessage> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public void Complete() => _channel.Writer.TryComplete();
}
