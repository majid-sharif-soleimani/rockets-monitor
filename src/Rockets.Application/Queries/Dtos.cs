using Rockets.Domain.Rockets;

namespace Rockets.Application.Queries;

public enum RocketStatusDto
{
    NotLaunched,
    Active,
    Exploded,
}

/// <summary>The user-facing state of a rocket.</summary>
public sealed record RocketDto(
    string Channel,
    bool Launched,
    string? Type,
    long Speed,
    string? Mission,
    RocketStatusDto Status,
    string? ExplosionReason,
    DateTimeOffset? LaunchedAt,
    DateTimeOffset? LastUpdatedAt)
{
    internal static RocketDto From(RocketState state) => new(
        state.Channel,
        state.Launched,
        state.Type,
        state.Speed,
        state.Mission,
        ToDto(state.Status),
        state.ExplosionReason,
        state.LaunchedAt,
        state.LastUpdatedAt);

    internal static RocketStatusDto ToDto(RocketStatus status) => status switch
    {
        RocketStatus.NotLaunched => RocketStatusDto.NotLaunched,
        RocketStatus.Active => RocketStatusDto.Active,
        RocketStatus.Exploded => RocketStatusDto.Exploded,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record FleetSummaryDto(
    int Total,
    int Active,
    int Exploded,
    int NotLaunched,
    IReadOnlyDictionary<string, int> ByType,
    IReadOnlyDictionary<string, int> ByMission);
