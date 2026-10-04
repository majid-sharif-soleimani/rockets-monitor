using Rockets.Domain.Messages;

namespace Rockets.Domain.Rockets;

public enum ApplyResult
{
    Applied,
    Duplicate,
    ConflictingLaunchIgnored,
    ConflictingExplosionIgnored,
}

/// <summary>
/// Keeps the latest state of one rocket up to date as its messages arrive, in any order.
/// </summary>
public interface IRocketMonitor
{
    string Channel { get; }

    /// <summary>The latest state. Safe to read from any thread.</summary>
    RocketState Current { get; }

    /// <summary>Applies a message. Must be called from a single writer per rocket.</summary>
    ApplyResult Apply(RocketMessage message);
}
