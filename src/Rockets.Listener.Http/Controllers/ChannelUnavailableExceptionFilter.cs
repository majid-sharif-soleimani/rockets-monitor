using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rockets.Application.Messaging;

namespace Rockets.Listener.Http.Controllers;

/// <summary>Maps a full or closed channel to 503 with <c>Retry-After</c>, so the sender redelivers.</summary>
public sealed partial class ChannelUnavailableExceptionFilter(
    IOptions<HttpListenerOptions> options,
    ILogger<ChannelUnavailableExceptionFilter> logger) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not MessageChannelUnavailableException exception)
        {
            return;
        }

        LogUnavailable(exception.Message);
        context.HttpContext.Response.Headers.RetryAfter = options.Value.RetryAfterSeconds.ToString();
        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = "Service unavailable",
            Detail = exception.Message,
        })
        {
            StatusCode = StatusCodes.Status503ServiceUnavailable,
        };
        context.ExceptionHandled = true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected message with 503: {Reason}")]
    private partial void LogUnavailable(string reason);
}
