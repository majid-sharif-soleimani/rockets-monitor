using Microsoft.AspNetCore.Mvc;
using Rockets.Application.Queries;

namespace Rockets.Api.Controllers;

/// <summary>Reports on the whole fleet of rockets.</summary>
[ApiController]
[Route("api/fleet")]
[Produces("application/json")]
public sealed class FleetController(IFleetReportService fleet) : ControllerBase
{
    /// <summary>Lists rockets with sorting, filtering by status and paging.</summary>
    /// <remarks>Missing values (e.g. the type of a rocket that has not launched) always sort last.</remarks>
    [HttpGet("rockets")]
    [ProducesResponseType<PagedResult<RocketDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public PagedResult<RocketDto> GetRockets([FromQuery] FleetQuery query) => fleet.GetRockets(query);

    /// <summary>Counts rockets by status, type and mission.</summary>
    [HttpGet("summary")]
    [ProducesResponseType<FleetSummaryDto>(StatusCodes.Status200OK)]
    public FleetSummaryDto GetSummary() => fleet.GetSummary();
}
