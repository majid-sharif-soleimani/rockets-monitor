using System.ComponentModel.DataAnnotations;

namespace Rockets.Application.Queries;

public enum RocketSortField
{
    Channel,
    Type,
    Speed,
    Mission,
    Status,
    LaunchedAt,
    LastUpdatedAt,
}

public enum SortOrder
{
    Asc,
    Desc,
}

/// <summary>Sorting, filtering and paging for the fleet list.</summary>
public sealed class FleetQuery
{
    public const int MaxPageSize = 500;

    public RocketSortField SortBy { get; init; } = RocketSortField.Channel;

    public SortOrder Order { get; init; } = SortOrder.Asc;

    /// <summary>Only return rockets with this status.</summary>
    public RocketStatusDto? Status { get; init; }

    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, MaxPageSize)]
    public int PageSize { get; init; } = 50;
}
