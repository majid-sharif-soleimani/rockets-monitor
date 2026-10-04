using Rockets.Domain.Messages;
using Rockets.Domain.Rockets;

namespace Rockets.Domain.Tests;

public class RocketMonitorTests
{
    private const string Channel = "193270a9-c9cf-404a-8f83-838e71d9ae67";
    private static readonly DateTimeOffset T0 = new(2022, 2, 2, 19, 39, 5, TimeSpan.FromHours(1));

    private static RocketLaunched Launched(long number, string type = "Falcon-9", long speed = 500, string mission = "ARTEMIS") =>
        new(Channel, number, T0.AddSeconds(number), type, speed, mission);

    private static RocketSpeedIncreased Increased(long number, long by) => new(Channel, number, T0.AddSeconds(number), by);
    private static RocketSpeedDecreased Decreased(long number, long by) => new(Channel, number, T0.AddSeconds(number), by);
    private static RocketMissionChanged MissionChanged(long number, string mission) => new(Channel, number, T0.AddSeconds(number), mission);
    private static RocketExploded Exploded(long number, string reason) => new(Channel, number, T0.AddSeconds(number), reason);

    [Fact]
    public void New_monitor_reports_a_rocket_that_has_not_launched()
    {
        var monitor = new RocketMonitor(Channel);

        var state = monitor.Current;

        Assert.Equal(Channel, state.Channel);
        Assert.False(state.Launched);
        Assert.Equal(RocketStatus.NotLaunched, state.Status);
        Assert.Null(state.Type);
        Assert.Equal(0, state.Speed);
    }

    [Fact]
    public void Launch_sets_type_speed_mission_and_status()
    {
        var monitor = new RocketMonitor(Channel);

        var result = monitor.Apply(Launched(1));

        Assert.Equal(ApplyResult.Applied, result);
        var state = monitor.Current;
        Assert.True(state.Launched);
        Assert.Equal(RocketStatus.Active, state.Status);
        Assert.Equal("Falcon-9", state.Type);
        Assert.Equal(500, state.Speed);
        Assert.Equal("ARTEMIS", state.Mission);
        Assert.Equal(T0.AddSeconds(1), state.LaunchedAt);
        Assert.Equal(T0.AddSeconds(1), state.LastUpdatedAt);
    }

    [Fact]
    public void Speed_changes_are_added_to_launch_speed()
    {
        var monitor = new RocketMonitor(Channel);

        monitor.Apply(Launched(1, speed: 500));
        monitor.Apply(Increased(2, 3000));
        monitor.Apply(Decreased(3, 2500));

        Assert.Equal(1000, monitor.Current.Speed);
    }

    [Fact]
    public void Speed_changes_received_before_the_launch_are_kept()
    {
        var monitor = new RocketMonitor(Channel);

        monitor.Apply(Increased(2, 3000));
        Assert.False(monitor.Current.Launched);
        Assert.Equal(3000, monitor.Current.Speed);

        monitor.Apply(Launched(1, speed: 500));

        Assert.Equal(3500, monitor.Current.Speed);
    }

    [Fact]
    public void Speed_is_not_clamped_at_zero()
    {
        var monitor = new RocketMonitor(Channel);

        monitor.Apply(Decreased(2, 100));

        Assert.Equal(-100, monitor.Current.Speed);
    }

    [Fact]
    public void Mission_from_the_highest_message_number_wins_regardless_of_arrival_order()
    {
        var monitor = new RocketMonitor(Channel);

        monitor.Apply(MissionChanged(3, "SHUTTLE_MIR"));
        monitor.Apply(MissionChanged(2, "APOLLO"));
        monitor.Apply(Launched(1, mission: "ARTEMIS"));

        Assert.Equal("SHUTTLE_MIR", monitor.Current.Mission);
    }

    [Fact]
    public void Explosion_is_permanent_and_keeps_the_reason()
    {
        var monitor = new RocketMonitor(Channel);

        monitor.Apply(Launched(1));
        monitor.Apply(Exploded(2, "PRESSURE_VESSEL_FAILURE"));
        monitor.Apply(Increased(3, 100));

        var state = monitor.Current;
        Assert.Equal(RocketStatus.Exploded, state.Status);
        Assert.Equal("PRESSURE_VESSEL_FAILURE", state.ExplosionReason);
        Assert.True(state.Launched);
    }

    [Fact]
    public void Explosion_before_launch_is_reported_as_exploded()
    {
        var monitor = new RocketMonitor(Channel);

        monitor.Apply(Exploded(5, "ENGINE_FAILURE"));

        Assert.Equal(RocketStatus.Exploded, monitor.Current.Status);
        Assert.False(monitor.Current.Launched);
    }

    [Fact]
    public void Second_launch_with_a_different_number_is_ignored()
    {
        var monitor = new RocketMonitor(Channel);

        monitor.Apply(Launched(1, type: "Falcon-9", speed: 500));
        var result = monitor.Apply(Launched(4, type: "Saturn-V", speed: 900));

        Assert.Equal(ApplyResult.ConflictingLaunchIgnored, result);
        Assert.Equal("Falcon-9", monitor.Current.Type);
        Assert.Equal(500, monitor.Current.Speed);
    }

    [Fact]
    public void Second_explosion_with_a_different_number_is_ignored()
    {
        var monitor = new RocketMonitor(Channel);

        monitor.Apply(Exploded(2, "FIRST"));
        var result = monitor.Apply(Exploded(3, "SECOND"));

        Assert.Equal(ApplyResult.ConflictingExplosionIgnored, result);
        Assert.Equal("FIRST", monitor.Current.ExplosionReason);
    }

    [Fact]
    public void Duplicate_messages_are_ignored()
    {
        var monitor = new RocketMonitor(Channel);

        monitor.Apply(Launched(1, speed: 500));
        monitor.Apply(Increased(3, 1000)); // above the watermark (2 is missing)

        Assert.Equal(ApplyResult.Duplicate, monitor.Apply(Launched(1, speed: 500))); // at/below the watermark
        Assert.Equal(ApplyResult.Duplicate, monitor.Apply(Increased(3, 1000)));    // in the set above the watermark
        Assert.Equal(1500, monitor.Current.Speed);
    }

    [Fact]
    public void Same_number_with_different_content_keeps_the_first_message()
    {
        var monitor = new RocketMonitor(Channel);

        monitor.Apply(Increased(2, 100));
        var result = monitor.Apply(Increased(2, 999));

        Assert.Equal(ApplyResult.Duplicate, result);
        Assert.Equal(100, monitor.Current.Speed);
    }

    [Fact]
    public void Last_updated_is_the_latest_message_time_seen()
    {
        var monitor = new RocketMonitor(Channel);

        monitor.Apply(Increased(5, 1));
        monitor.Apply(Launched(1));

        Assert.Equal(T0.AddSeconds(5), monitor.Current.LastUpdatedAt);
    }

    [Fact]
    public void Message_for_another_channel_is_rejected()
    {
        var monitor = new RocketMonitor(Channel);

        Assert.Throws<ArgumentException>(() =>
            monitor.Apply(new RocketSpeedIncreased("other", 1, T0, 10)));
    }

    [Fact]
    public void Final_state_does_not_depend_on_arrival_order_or_duplicates()
    {
        RocketMessage[] messages =
        [
            Launched(1, speed: 500, mission: "ARTEMIS"),
            Increased(2, 3000),
            MissionChanged(3, "APOLLO"),
            Decreased(4, 2500),
            Increased(5, 700),
            MissionChanged(6, "SHUTTLE_MIR"),
            Decreased(7, 50),
            Exploded(8, "PRESSURE_VESSEL_FAILURE"),
        ];

        var expected = ApplyAll(messages);

        var random = new Random(444);
        for (var run = 0; run < 50; run++)
        {
            // Shuffle and redeliver some messages to simulate at-least-once delivery.
            var delivery = messages
                .Concat(messages.Where(_ => random.Next(3) == 0))
                .OrderBy(_ => random.Next())
                .ToArray();

            Assert.Equal(expected, ApplyAll(delivery));
        }

        Assert.Equal(1650, expected.Speed);
        Assert.Equal("SHUTTLE_MIR", expected.Mission);
        Assert.Equal(RocketStatus.Exploded, expected.Status);
    }

    private static RocketState ApplyAll(IEnumerable<RocketMessage> messages)
    {
        var monitor = new RocketMonitor(Channel);
        foreach (var message in messages)
        {
            monitor.Apply(message);
        }

        return monitor.Current;
    }
}
