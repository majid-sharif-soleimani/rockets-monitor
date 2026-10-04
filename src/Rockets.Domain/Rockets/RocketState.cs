namespace Rockets.Domain.Rockets;

public enum RocketStatus
{
    NotLaunched,
    Active,
    Exploded,
}

/// <summary>
/// Immutable snapshot of what is currently known about a rocket.
/// </summary>
public sealed record RocketState(
    string Channel,
    bool Launched,
    string? Type,
    long Speed,
    string? Mission,
    RocketStatus Status,
    string? ExplosionReason,
    DateTimeOffset? LaunchedAt,
    DateTimeOffset? LastUpdatedAt)
{
    public static RocketState Initial(string channel) =>
        new(channel, false, null, 0, null, RocketStatus.NotLaunched, null, null, null);
}
