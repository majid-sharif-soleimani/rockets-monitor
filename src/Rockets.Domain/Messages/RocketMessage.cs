namespace Rockets.Domain.Messages;

/// <summary>
/// A message sent by a rocket on its own radio channel. The channel identifies the rocket and
/// <see cref="MessageNumber"/> is the message's position within that channel.
/// </summary>
public abstract record RocketMessage(string Channel, long MessageNumber, DateTimeOffset MessageTime);

public sealed record RocketLaunched(
    string Channel, long MessageNumber, DateTimeOffset MessageTime,
    string Type, long LaunchSpeed, string Mission)
    : RocketMessage(Channel, MessageNumber, MessageTime);

public sealed record RocketSpeedIncreased(string Channel, long MessageNumber, DateTimeOffset MessageTime, long By)
    : RocketMessage(Channel, MessageNumber, MessageTime);

public sealed record RocketSpeedDecreased(string Channel, long MessageNumber, DateTimeOffset MessageTime, long By)
    : RocketMessage(Channel, MessageNumber, MessageTime);

public sealed record RocketExploded(string Channel, long MessageNumber, DateTimeOffset MessageTime, string Reason)
    : RocketMessage(Channel, MessageNumber, MessageTime);

public sealed record RocketMissionChanged(string Channel, long MessageNumber, DateTimeOffset MessageTime, string NewMission)
    : RocketMessage(Channel, MessageNumber, MessageTime);
