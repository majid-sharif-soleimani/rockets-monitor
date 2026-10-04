namespace Rockets.Domain.Rockets;

/// <summary>
/// Remembers which message numbers of a channel have been applied, so redelivered messages can be
/// detected. Every number up to <see cref="Watermark"/> has been applied; numbers above it that
/// arrived early are kept in a sorted set until the gap below them fills.
/// </summary>
/// <remarks>Not thread-safe: a rocket has a single writer.</remarks>
internal sealed class MessageNumberTracker
{
    private readonly SortedSet<long> _appliedAboveWatermark = [];

    public long Watermark { get; private set; }

    public int PendingCount => _appliedAboveWatermark.Count;

    /// <summary>Marks <paramref name="number"/> as applied. Returns false if it already was.</summary>
    public bool TryMarkApplied(long number)
    {
        if (number <= Watermark || !_appliedAboveWatermark.Add(number))
        {
            return false;
        }

        while (_appliedAboveWatermark.Count > 0 && _appliedAboveWatermark.Min == Watermark + 1)
        {
            _appliedAboveWatermark.Remove(Watermark + 1);
            Watermark++;
        }

        return true;
    }
}
