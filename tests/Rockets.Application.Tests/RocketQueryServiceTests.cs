using Rockets.Application.Queries;
using Rockets.Domain.Messages;
using Rockets.Domain.Rockets;
using Rockets.Infrastructure.Rockets;

namespace Rockets.Application.Tests;

public class RocketQueryServiceTests
{
    [Fact]
    public void Returns_the_current_state_of_a_known_rocket()
    {
        var registry = new RocketRegistry(new RocketMonitorFactory());
        registry.GetOrCreate("a").Apply(new RocketLaunched("a", 1, DateTimeOffset.UnixEpoch, "Falcon-9", 500, "ARTEMIS"));
        var service = new RocketQueryService(registry);

        var rocket = service.GetRocket("a");

        Assert.NotNull(rocket);
        Assert.Equal("Falcon-9", rocket.Type);
        Assert.Equal(500, rocket.Speed);
        Assert.Equal(RocketStatusDto.Active, rocket.Status);
    }

    [Fact]
    public void Returns_null_for_an_unknown_rocket()
    {
        var service = new RocketQueryService(new RocketRegistry(new RocketMonitorFactory()));

        Assert.Null(service.GetRocket("missing"));
    }
}
