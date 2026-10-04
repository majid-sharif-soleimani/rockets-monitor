using Rockets.Application.Queries;
using Rockets.Domain.Messages;
using Rockets.Domain.Rockets;

namespace Rockets.Application.Tests;

public class FleetReportServiceTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    private readonly RocketRegistry _registry = new();
    private readonly FleetReportService _service;

    public FleetReportServiceTests()
    {
        _service = new FleetReportService(_registry);

        Launch("b", "Falcon-9", 300, "ARTEMIS");
        Launch("a", "Saturn-V", 900, "APOLLO");
        Launch("c", "Falcon-9", 100, "ARTEMIS");
        _registry.GetOrCreate("c").Apply(new RocketExploded("c", 2, T0, "ENGINE_FAILURE"));
        _registry.GetOrCreate("d").Apply(new RocketSpeedIncreased("d", 2, T0, 50)); // not launched yet
    }

    private void Launch(string channel, string type, long speed, string mission) =>
        _registry.GetOrCreate(channel).Apply(new RocketLaunched(channel, 1, T0, type, speed, mission));

    private string[] Channels(FleetQuery query) =>
        _service.GetRockets(query).Items.Select(r => r.Channel).ToArray();

    [Fact]
    public void Default_query_sorts_by_channel_ascending()
    {
        Assert.Equal(["a", "b", "c", "d"], Channels(new FleetQuery()));
    }

    [Fact]
    public void Sorts_by_speed_descending()
    {
        Assert.Equal(["a", "b", "c", "d"], Channels(new FleetQuery { SortBy = RocketSortField.Speed, Order = SortOrder.Desc }));
    }

    [Fact]
    public void Missing_values_sort_last_in_both_directions_and_ties_break_by_channel()
    {
        Assert.Equal(["b", "c", "a", "d"], Channels(new FleetQuery { SortBy = RocketSortField.Type, Order = SortOrder.Asc }));
        Assert.Equal(["a", "b", "c", "d"], Channels(new FleetQuery { SortBy = RocketSortField.Type, Order = SortOrder.Desc }));
    }

    [Fact]
    public void Filters_by_status()
    {
        Assert.Equal(["a", "b"], Channels(new FleetQuery { Status = RocketStatusDto.Active }));
        Assert.Equal(["c"], Channels(new FleetQuery { Status = RocketStatusDto.Exploded }));
        Assert.Equal(["d"], Channels(new FleetQuery { Status = RocketStatusDto.NotLaunched }));
    }

    [Fact]
    public void Pages_the_result()
    {
        var page = _service.GetRockets(new FleetQuery { Page = 2, PageSize = 3 });

        Assert.Equal(["d"], page.Items.Select(r => r.Channel));
        Assert.Equal(4, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
    }

    [Fact]
    public void Summary_counts_rockets_by_status_type_and_mission()
    {
        var summary = _service.GetSummary();

        Assert.Equal(4, summary.Total);
        Assert.Equal(2, summary.Active);
        Assert.Equal(1, summary.Exploded);
        Assert.Equal(1, summary.NotLaunched);
        Assert.Equal(2, summary.ByType["Falcon-9"]);
        Assert.Equal(1, summary.ByType["Saturn-V"]);
        Assert.Equal(2, summary.ByMission["ARTEMIS"]);
    }
}
