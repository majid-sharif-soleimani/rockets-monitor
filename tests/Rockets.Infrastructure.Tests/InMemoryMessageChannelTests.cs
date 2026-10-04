using Microsoft.Extensions.Options;
using Rockets.Application.Messaging;
using Rockets.Domain.Messages;
using Rockets.Infrastructure.Messaging;

namespace Rockets.Infrastructure.Tests;

public class InMemoryMessageChannelTests
{
    private static InMemoryMessageChannel CreateChannel(int capacity = 10, int writeTimeoutMs = 50) =>
        new(Options.Create(new MessageChannelOptions
        {
            Capacity = capacity,
            WriteTimeout = TimeSpan.FromMilliseconds(writeTimeoutMs),
        }));

    private static RocketMessage Message(long number) =>
        new RocketSpeedIncreased("rocket", number, DateTimeOffset.UnixEpoch, 1);

    [Fact]
    public async Task Messages_are_read_in_the_order_written()
    {
        var channel = CreateChannel();

        await channel.WriteAsync(Message(1), CancellationToken.None);
        await channel.WriteAsync(Message(2), CancellationToken.None);
        channel.Complete();

        var read = new List<long>();
        await foreach (var message in channel.ReadAllAsync(CancellationToken.None))
        {
            read.Add(message.MessageNumber);
        }

        Assert.Equal([1, 2], read);
    }

    [Fact]
    public async Task Writing_to_a_full_channel_throws_after_the_timeout()
    {
        var channel = CreateChannel(capacity: 1);
        await channel.WriteAsync(Message(1), CancellationToken.None);

        await Assert.ThrowsAsync<MessageChannelFullException>(
            () => channel.WriteAsync(Message(2), CancellationToken.None).AsTask());
        Assert.Equal(1, channel.Count);
    }

    [Fact]
    public async Task Write_waits_for_space_within_the_timeout()
    {
        var channel = CreateChannel(capacity: 1, writeTimeoutMs: 2000);
        await channel.WriteAsync(Message(1), CancellationToken.None);

        var write = channel.WriteAsync(Message(2), CancellationToken.None).AsTask();
        await using var reader = channel.ReadAllAsync(CancellationToken.None).GetAsyncEnumerator();
        await reader.MoveNextAsync();

        await write;
        Assert.Equal(1, channel.Count);
    }

    [Fact]
    public async Task Writing_to_a_completed_channel_throws()
    {
        var channel = CreateChannel();
        channel.Complete();

        await Assert.ThrowsAsync<MessageChannelUnavailableException>(
            () => channel.WriteAsync(Message(1), CancellationToken.None).AsTask());
    }
}
