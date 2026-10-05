using System.Collections.Concurrent;

namespace Rockets.Domain.Rockets;

public sealed class RocketRegistry : IRocketRegistry
{
    private readonly ConcurrentDictionary<string, IRocketMonitor> _monitors = new();

    public int Count => _monitors.Count;


    public IRocketMonitor GetOrCreate(string channel) =>
        _monitors.GetOrAdd(channel, static c => new RocketMonitor(c));

    public IRocketMonitor? Find(string channel) =>
        _monitors.TryGetValue(channel, out var monitor) ? monitor : null;

    public IReadOnlyCollection<RocketState> GetAllStates() =>
        _monitors.Values.Select(m => m.Current).ToArray();
}
