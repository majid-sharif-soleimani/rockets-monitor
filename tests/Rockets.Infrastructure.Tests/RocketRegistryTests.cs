using Rockets.Domain.Messages;
using Rockets.Domain.Rockets;
using Rockets.Infrastructure.Rockets;

namespace Rockets.Infrastructure.Tests;

public class RocketRegistryTests
{
    [Fact]
    public void GetOrCreate_returns_the_same_monitor_for_the_same_channel()
    {
        var registry = new RocketRegistry(new RocketMonitorFactory());

        var first = registry.GetOrCreate("a");
        var second = registry.GetOrCreate("a");

        Assert.Same(first, second);
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void Find_returns_null_for_unknown_channel()
    {
        var registry = new RocketRegistry(new RocketMonitorFactory());

        Assert.Null(registry.Find("missing"));
    }

    [Fact]
    public void GetAllStates_returns_the_current_state_of_every_rocket()
    {
        var registry = new RocketRegistry(new RocketMonitorFactory());
        registry.GetOrCreate("a").Apply(new RocketSpeedIncreased("a", 2, DateTimeOffset.UnixEpoch, 10));
        registry.GetOrCreate("b");

        var states = registry.GetAllStates();

        Assert.Equal(["a", "b"], states.Select(s => s.Channel).Order());
        Assert.Equal(10, states.Single(s => s.Channel == "a").Speed);
    }
}
