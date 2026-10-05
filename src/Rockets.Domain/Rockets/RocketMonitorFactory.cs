namespace Rockets.Domain.Rockets;

/// <summary>
/// Creates the monitor for a rocket. It lets a registry outside this project create monitors
/// without knowing the implementation.
/// </summary>
public interface IRocketMonitorFactory
{
    IRocketMonitor Create(string channel);
}

public sealed class RocketMonitorFactory : IRocketMonitorFactory
{
    public IRocketMonitor Create(string channel) => new RocketMonitor(channel);
}
