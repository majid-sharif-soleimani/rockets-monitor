using Microsoft.AspNetCore.Mvc;
using Rockets.Application.Queries;

namespace Rockets.Api.Controllers;

/// <summary>Reports on individual rockets.</summary>
[ApiController]
[Route("api/rockets")]
[Produces("application/json")]
public sealed class RocketsController(IRocketQueryService rockets) : ControllerBase
{
    /// <summary>Returns the current state of a rocket.</summary>
    /// <param name="channel">The rocket's radio channel, which identifies it.</param>
    [HttpGet("{channel}")]
    [ProducesResponseType<RocketDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<RocketDto> Get(string channel) =>
        rockets.GetRocket(channel) is { } rocket
            ? rocket
            : Problem(statusCode: StatusCodes.Status404NotFound, detail: $"Rocket '{channel}' is unknown.");
}
