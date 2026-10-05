using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Rockets.Application.Queries;

namespace Rockets.Api.Endpoints;

/// <summary>Reports on the whole fleet of rockets.</summary>
public static class FleetEndpoints
{
    public static IEndpointRouteBuilder MapFleetEndpoints(this IEndpointRouteBuilder app)
    {
        var fleet = app.MapGroup("/api/fleet").WithTags("Fleet");

        fleet.MapGet("/rockets", GetRockets)
            .Produces<PagedResult<RocketDto>>()
            .ProducesValidationProblem();

        fleet.MapGet("/summary", GetSummary)
            .Produces<FleetSummaryDto>();

        return app;
    }

    /// <summary>Lists rockets with sorting, filtering by status and paging.</summary>
    /// <remarks>Missing values (e.g. the type of a rocket that has not launched) always sort last.</remarks>
    /// <param name="fleet"></param>
    /// <param name="sortBy">channel (default), type, speed, mission, status, launchedAt or lastUpdatedAt.</param>
    /// <param name="order">asc (default) or desc.</param>
    /// <param name="status">Only return rockets with this status: notLaunched, active or exploded.</param>
    /// <param name="page">Page number, starting at 1. Default: 1.</param>
    /// <param name="pageSize">Rockets per page, 1 to 500. Default: 50.</param>
    private static IResult GetRockets(
        IFleetReportService fleet,
        string? sortBy = null,
        string? order = null,
        string? status = null,
        int page = 1,
        int pageSize = 50)
    {
        var errors = new Dictionary<string, string[]>();

        // Enum values are read as text and parsed here, because minimal APIs bind enums
        // case-sensitively and the API uses camelCase values (e.g. "notLaunched").
        var query = new FleetQuery
        {
            SortBy = ParseEnum<RocketSortField>(sortBy, nameof(sortBy), errors) ?? RocketSortField.Channel,
            Order = ParseEnum<SortOrder>(order, nameof(order), errors) ?? SortOrder.Asc,
            Status = ParseEnum<RocketStatusDto>(status, nameof(status), errors),
            Page = page,
            PageSize = pageSize,
        };

        // Minimal APIs do not validate data annotations on their own.
        var invalid = new List<ValidationResult>();
        Validator.TryValidateObject(query, new ValidationContext(query), invalid, validateAllProperties: true);
        foreach (var result in invalid)
        {
            foreach (var member in result.MemberNames)
            {
                errors[JsonNamingPolicy.CamelCase.ConvertName(member)] = [result.ErrorMessage ?? "Invalid value."];
            }
        }

        return errors.Count > 0
            ? Results.ValidationProblem(errors)
            : Results.Ok(fleet.GetRockets(query));
    }

    /// <summary>Counts rockets by status, type and mission.</summary>
    /// <param name="fleet"></param>
    private static FleetSummaryDto GetSummary(IFleetReportService fleet) => fleet.GetSummary();

    private static TEnum? ParseEnum<TEnum>(string? value, string name, Dictionary<string, string[]> errors)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        errors[name] = [$"'{value}' is not valid. Allowed values: {string.Join(", ", Enum.GetNames<TEnum>().Select(JsonNamingPolicy.CamelCase.ConvertName))}."];
        return null;
    }
}
