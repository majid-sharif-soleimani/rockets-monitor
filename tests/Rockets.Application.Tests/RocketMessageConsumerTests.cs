using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rockets.Application.Consumers;
using Rockets.Domain.Messages;
using Rockets.Domain.Rockets;
using Rockets.Infrastructure.Messaging;

namespace Rockets.Application.Tests;

public class RocketMessageConsumerTests
{
    private readonly InMemoryMessageChannel _channel = new(Options.Create(new MessageChannelOptions()));

    [Fact]
    public async Task Applies_every_message_written_before_stopping()
    {
        var registry = new RocketRegistry();
        var consumer = new RocketMessageConsumer(_channel, registry, NullLogger<RocketMessageConsumer>.Instance);

        await consumer.StartAsync(CancellationToken.None);
        await _channel.WriteAsync(new RocketLaunched("a", 1, DateTimeOffset.UnixEpoch, "Falcon-9", 500, "ARTEMIS"), CancellationToken.None);
        await _channel.WriteAsync(new RocketSpeedIncreased("a", 2, DateTimeOffset.UnixEpoch, 100), CancellationToken.None);
        await consumer.StopAsync(CancellationToken.None);

        Assert.Equal(600, registry.Find("a")!.Current.Speed);
    }

    [Fact]
    public async Task A_failing_message_does_not_stop_the_consumer()
    {
        var registry = new FailingRegistry(failingChannel: "bad");
        var consumer = new RocketMessageConsumer(_channel, registry, NullLogger<RocketMessageConsumer>.Instance);

        await consumer.StartAsync(CancellationToken.None);
        await _channel.WriteAsync(new RocketSpeedIncreased("bad", 1, DateTimeOffset.UnixEpoch, 1), CancellationToken.None);
        await _channel.WriteAsync(new RocketSpeedIncreased("good", 1, DateTimeOffset.UnixEpoch, 7), CancellationToken.None);
        await consumer.StopAsync(CancellationToken.None);

        Assert.Equal(7, registry.Find("good")!.Current.Speed);
    }

    private sealed class FailingRegistry(string failingChannel) : IRocketRegistry
    {
        private readonly RocketRegistry _inner = new();

        public int Count => _inner.Count;

        public IRocketMonitor GetOrCreate(string channel) =>
            channel == failingChannel ? throw new InvalidOperationException("boom") : _inner.GetOrCreate(channel);

        public IRocketMonitor? Find(string channel) => _inner.Find(channel);

        public IReadOnlyCollection<RocketState> GetAllStates() => _inner.GetAllStates();
    }
}
