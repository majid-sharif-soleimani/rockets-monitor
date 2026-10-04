using Rockets.Domain.Rockets;

namespace Rockets.Application.Queries;

/// <summary>Reports on the collection of all rockets.</summary>
public interface IFleetReportService
{
    PagedResult<RocketDto> GetRockets(FleetQuery query);

    FleetSummaryDto GetSummary();
}

/// <summary>
/// Builds reports from the current rocket snapshots. Sorting happens on every read, which is
/// O(n log n) and fine for thousands of rockets (see DEC-11).
/// </summary>
public sealed class FleetReportService(IRocketRegistry registry) : IFleetReportService
{
    public PagedResult<RocketDto> GetRockets(FleetQuery query)
    {
        var rockets = registry.GetAllStates().Select(RocketDto.From);

        if (query.Status is { } status)
        {
            rockets = rockets.Where(r => r.Status == status);
        }

        var sorted = Sort(rockets, query.SortBy, query.Order == SortOrder.Desc).ToList();
        var page = sorted
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        return new PagedResult<RocketDto>(page, query.Page, query.PageSize, sorted.Count);
    }

    public FleetSummaryDto GetSummary()
    {
        var rockets = registry.GetAllStates();

        return new FleetSummaryDto(
            Total: rockets.Count,
            Active: rockets.Count(r => r.Status == RocketStatus.Active),
            Exploded: rockets.Count(r => r.Status == RocketStatus.Exploded),
            NotLaunched: rockets.Count(r => r.Status == RocketStatus.NotLaunched),
            ByType: CountBy(rockets, r => r.Type),
            ByMission: CountBy(rockets, r => r.Mission));
    }

    private static IEnumerable<RocketDto> Sort(IEnumerable<RocketDto> rockets, RocketSortField field, bool descending) =>
        field switch
        {
            RocketSortField.Channel => OrderBy(rockets, r => r.Channel, descending, StringComparer.Ordinal),
            RocketSortField.Type => OrderBy(rockets, r => r.Type, descending, StringComparer.Ordinal),
            RocketSortField.Speed => OrderBy(rockets, r => (long?)r.Speed, descending),
            RocketSortField.Mission => OrderBy(rockets, r => r.Mission, descending, StringComparer.Ordinal),
            RocketSortField.Status => OrderBy(rockets, r => (RocketStatusDto?)r.Status, descending),
            RocketSortField.LaunchedAt => OrderBy(rockets, r => r.LaunchedAt, descending),
            RocketSortField.LastUpdatedAt => OrderBy(rockets, r => r.LastUpdatedAt, descending),
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
        };

    /// <summary>Missing values always sort last; ties break by channel so paging is stable.</summary>
    private static IEnumerable<RocketDto> OrderBy<TKey>(IEnumerable<RocketDto> rockets, Func<RocketDto, TKey?> key, bool descending, IComparer<TKey?>? comparer = null)
    {
        var withNullsLast = rockets.OrderBy(r => key(r) is null);
        var byKey = descending ? withNullsLast.ThenByDescending(key, comparer) : withNullsLast.ThenBy(key, comparer);
        return byKey.ThenBy(r => r.Channel, StringComparer.Ordinal);
    }

    private static Dictionary<string, int> CountBy(IEnumerable<RocketState> rockets, Func<RocketState, string?> key) =>
        rockets
            .Select(key)
            .OfType<string>()
            .GroupBy(value => value)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count());
}
