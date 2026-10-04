namespace Rockets.Domain.Rockets;

/// <summary>
/// Keeps track of every rocket seen so far.
/// </summary>
public interface IRocketRegistry
{
    int Count { get; }

    /// <summary>Returns the rocket's monitor, creating it the first time the channel is seen.</summary>
    IRocketMonitor GetOrCreate(string channel);

    IRocketMonitor? Find(string channel);

    IReadOnlyCollection<RocketState> GetAllStates();
}
