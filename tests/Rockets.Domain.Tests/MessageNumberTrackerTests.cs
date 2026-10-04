using Rockets.Domain.Rockets;

namespace Rockets.Domain.Tests;

public class MessageNumberTrackerTests
{
    [Fact]
    public void Contiguous_numbers_advance_the_watermark()
    {
        var tracker = new MessageNumberTracker();

        Assert.True(tracker.TryMarkApplied(1));
        Assert.True(tracker.TryMarkApplied(2));

        Assert.Equal(2, tracker.Watermark);
        Assert.Equal(0, tracker.PendingCount);
    }

    [Fact]
    public void Numbers_above_a_gap_are_kept_until_the_gap_fills()
    {
        var tracker = new MessageNumberTracker();

        tracker.TryMarkApplied(1);
        tracker.TryMarkApplied(3);
        tracker.TryMarkApplied(4);
        Assert.Equal(1, tracker.Watermark);
        Assert.Equal(2, tracker.PendingCount);

        tracker.TryMarkApplied(2);

        Assert.Equal(4, tracker.Watermark);
        Assert.Equal(0, tracker.PendingCount);
    }

    [Fact]
    public void Already_applied_numbers_are_rejected()
    {
        var tracker = new MessageNumberTracker();

        tracker.TryMarkApplied(1);
        tracker.TryMarkApplied(5);

        Assert.False(tracker.TryMarkApplied(1));
        Assert.False(tracker.TryMarkApplied(5));
        Assert.True(tracker.TryMarkApplied(3));
    }
}
