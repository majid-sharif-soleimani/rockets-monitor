using Rockets.Application.Queries;

namespace Rockets.Api.Endpoints;

/// <summary>Reports on individual rockets.</summary>
public static class RocketEndpoints
{
    public static IEndpointRouteBuilder MapRocketEndpoints(this IEndpointRouteBuilder app)
    {
        var rockets = app.MapGroup("/api/rockets").WithTags("Rockets");

        rockets.MapGet("/{channel}", GetRocket)
            .Produces<RocketDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>Returns the current state of a rocket.</summary>
    /// <param name="channel">The rocket's radio channel, which identifies it.</param>
    /// <param name="rockets"></param>
    private static IResult GetRocket(string channel, IRocketQueryService rockets) =>
        rockets.GetRocket(channel) is { } rocket
            ? Results.Ok(rocket)
            : Results.Problem(statusCode: StatusCodes.Status404NotFound, detail: $"Rocket '{channel}' is unknown.");
}
