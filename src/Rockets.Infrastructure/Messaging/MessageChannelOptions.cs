namespace Rockets.Infrastructure.Messaging;

public sealed class MessageChannelOptions
{
    public const string SectionName = "Channel";

    /// <summary>Maximum number of messages waiting to be consumed (backpressure).</summary>
    public int Capacity { get; set; } = 10_000;

    /// <summary>How long a write waits for space before the channel is reported as full.</summary>
    public TimeSpan WriteTimeout { get; set; } = TimeSpan.FromMilliseconds(100);
}
