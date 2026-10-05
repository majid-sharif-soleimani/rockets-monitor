using System.Collections.Concurrent;
using Rockets.Domain.Rockets;

namespace Rockets.Infrastructure.Rockets;

/// <summary>
/// Keeps every rocket's monitor in memory. A shared store (e.g. Redis) can replace it by
/// implementing <see cref="IRocketRegistry"/>.
/// </summary>
internal sealed class RocketRegistry(IRocketMonitorFactory monitorFactory) : IRocketRegistry
{
    private readonly ConcurrentDictionary<string, IRocketMonitor> _monitors = new();

    public int Count => _monitors.Count;

    public IRocketMonitor GetOrCreate(string channel) =>
        _monitors.GetOrAdd(channel, monitorFactory.Create);

    public IRocketMonitor? Find(string channel) =>
        _monitors.TryGetValue(channel, out var monitor) ? monitor : null;

    public IReadOnlyCollection<RocketState> GetAllStates() =>
        _monitors.Values.Select(m => m.Current).ToArray();
}
