using Rockets.Domain.Messages;

namespace Rockets.Domain.Rockets;

/// <summary>
/// Calculates a rocket's state without waiting for missing messages. Every rule gives the same
/// result whatever the arrival order (see DEC-07 in decisions.md):
/// speed is a sum of deltas, the mission with the highest message number wins, and the first
/// launch and explosion received win.
/// </summary>
internal sealed class RocketMonitor : IRocketMonitor
{
    private readonly MessageNumberTracker _tracker = new();

    private RocketLaunched? _launch;
    private RocketExploded? _explosion;
    private long _speedDelta;
    private string? _mission;
    private long _missionMessageNumber;
    private DateTimeOffset? _lastUpdatedAt;

    private RocketState _current;

    public RocketMonitor(string channel)
    {
        Channel = channel;
        _current = RocketState.Initial(channel);
    }

    public string Channel { get; }

    public RocketState Current => Volatile.Read(ref _current);

    public ApplyResult Apply(RocketMessage message)
    {
        if (message.Channel != Channel)
        {
            throw new ArgumentException(
                $"Message for channel '{message.Channel}' cannot be applied to rocket '{Channel}'.", nameof(message));
        }

        if (!_tracker.TryMarkApplied(message.MessageNumber))
        {
            return ApplyResult.Duplicate;
        }

        var result = message switch
        {
            RocketLaunched launched => ApplyLaunch(launched),
            RocketSpeedIncreased increased => ApplySpeedChange(increased.By),
            RocketSpeedDecreased decreased => ApplySpeedChange(-decreased.By),
            RocketMissionChanged changed => ApplyMission(changed.NewMission, changed.MessageNumber),
            RocketExploded exploded => ApplyExplosion(exploded),
            _ => throw new ArgumentOutOfRangeException(nameof(message), message.GetType().Name, "Unsupported message type."),
        };

        if (result == ApplyResult.Applied)
        {
            if (_lastUpdatedAt is null || message.MessageTime > _lastUpdatedAt)
            {
                _lastUpdatedAt = message.MessageTime;
            }

            Volatile.Write(ref _current, BuildState());
        }

        return result;
    }

    private ApplyResult ApplyLaunch(RocketLaunched launched)
    {
        if (_launch is not null)
        {
            return ApplyResult.ConflictingLaunchIgnored;
        }

        _launch = launched;
        return ApplyMission(launched.Mission, launched.MessageNumber);
    }

    private ApplyResult ApplySpeedChange(long delta)
    {
        _speedDelta += delta;
        return ApplyResult.Applied;
    }

    private ApplyResult ApplyMission(string mission, long messageNumber)
    {
        if (messageNumber > _missionMessageNumber)
        {
            _mission = mission;
            _missionMessageNumber = messageNumber;
        }

        return ApplyResult.Applied;
    }

    private ApplyResult ApplyExplosion(RocketExploded exploded)
    {
        if (_explosion is not null)
        {
            return ApplyResult.ConflictingExplosionIgnored;
        }

        _explosion = exploded;
        return ApplyResult.Applied;
    }

    private RocketState BuildState()
    {
        var status = _explosion is not null ? RocketStatus.Exploded
            : _launch is not null ? RocketStatus.Active
            : RocketStatus.NotLaunched;

        return new RocketState(
            Channel,
            Launched: _launch is not null,
            Type: _launch?.Type,
            Speed: (_launch?.LaunchSpeed ?? 0) + _speedDelta,
            Mission: _mission,
            Status: status,
            ExplosionReason: _explosion?.Reason,
            LaunchedAt: _launch?.MessageTime,
            LastUpdatedAt: _lastUpdatedAt);
    }

    /// <summary>
    /// Remembers which message numbers of a channel have been applied, so redelivered messages can be
    /// detected. Every number up to <see cref="Watermark"/> has been applied; numbers above it that
    /// arrived early are kept in a sorted set until the gap below them fills.
    /// </summary>
    /// <remarks>Not thread-safe: a rocket has a single writer.</remarks>
    private sealed class MessageNumberTracker
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

}
