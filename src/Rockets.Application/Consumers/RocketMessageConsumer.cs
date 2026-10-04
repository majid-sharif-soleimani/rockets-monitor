using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rockets.Application.Messaging;
using Rockets.Domain.Messages;
using Rockets.Domain.Rockets;

namespace Rockets.Application.Consumers;

/// <summary>
/// The single consumer: reads the channel and applies each message to its rocket's monitor.
/// Being the only writer means monitors need no locks (see DEC-10).
/// </summary>
/// <remarks>
/// On shutdown the channel is completed and the consumer keeps reading until it is drained, so
/// messages that were already acknowledged are not lost. Register this service before the
/// listeners: hosted services stop in reverse order, so listeners stop accepting first.
/// </remarks>
public sealed partial class RocketMessageConsumer(
    IMessageChannel channel,
    IRocketRegistry registry,
    ILogger<RocketMessageConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Not bound to stoppingToken: reading ends when the channel is completed and drained.
        await foreach (var message in channel.ReadAllAsync(CancellationToken.None))
        {
            Process(message);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        channel.Complete();
        await base.StopAsync(cancellationToken);
    }

    private void Process(RocketMessage message)
    {
        try
        {
            if (registry.Find(message.Channel) is null)
            {
                LogNewRocket(message.Channel);
            }

            var result = registry.GetOrCreate(message.Channel).Apply(message);
            switch (result)
            {
                case ApplyResult.Applied:
                    LogApplied(message.Channel, message.MessageNumber, message.GetType().Name);
                    break;
                case ApplyResult.Duplicate:
                    LogDuplicate(message.Channel, message.MessageNumber);
                    break;
                default:
                    LogConflictIgnored(message.Channel, message.MessageNumber, result);
                    break;
            }
        }
        catch (Exception ex)
        {
            // The message was already acknowledged; keep consuming the rest.
            LogFailed(ex, message.Channel, message.MessageNumber);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "New rocket {Channel}")]
    private partial void LogNewRocket(string channel);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Applied {MessageType} #{MessageNumber} to rocket {Channel}")]
    private partial void LogApplied(string channel, long messageNumber, string messageType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Ignored duplicate #{MessageNumber} for rocket {Channel}")]
    private partial void LogDuplicate(string channel, long messageNumber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignored message #{MessageNumber} for rocket {Channel}: {Result}")]
    private partial void LogConflictIgnored(string channel, long messageNumber, ApplyResult result);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to apply message #{MessageNumber} for rocket {Channel}")]
    private partial void LogFailed(Exception exception, string channel, long messageNumber);
}
