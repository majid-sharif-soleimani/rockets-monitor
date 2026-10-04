using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Rockets.Application.Messaging;
using Rockets.Listener.Http.Parsing;

namespace Rockets.Listener.Http.Controllers;

[ApiController]
[Route("messages")]
public sealed partial class MessagesController(IMessageChannel channel, ILogger<MessagesController> logger) : ControllerBase
{
    /// <summary>Receives a rocket message.</summary>
    /// <remarks>
    /// 202 is returned only once the message is queued for processing. Any other status makes
    /// the sender redeliver the message later.
    /// </remarks>
    /// <response code="202">Queued, or ignored because the message type is unknown.</response>
    /// <response code="400">The message is malformed.</response>
    /// <response code="429">Rate limit exceeded; retry after <c>Retry-After</c>.</response>
    /// <response code="503">The queue is full; retry after <c>Retry-After</c>.</response>
    [HttpPost]
    [EnableRateLimiting(HttpMessageListener.RateLimitPolicy)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Post([FromBody] MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        var result = RocketMessageMapper.Map(envelope);
        switch (result.Status)
        {
            case MapStatus.Invalid:
                return Problem(statusCode: StatusCodes.Status400BadRequest, detail: result.Error);

            case MapStatus.UnknownType:
                // Acknowledged so the sender does not redeliver a message we will never understand.
                LogUnknownType(envelope.Metadata!.MessageType!, envelope.Metadata.Channel!, envelope.Metadata.MessageNumber);
                return Accepted();

            default:
                await channel.WriteAsync(result.Message!, cancellationToken);
                return Accepted();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignored unknown message type {MessageType} (#{MessageNumber} on {Channel})")]
    private partial void LogUnknownType(string messageType, string channel, long messageNumber);
}
