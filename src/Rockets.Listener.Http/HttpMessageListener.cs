using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Constraints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Rockets.Application.Messaging;
using Rockets.Listener.Http.Parsing;

namespace Rockets.Listener.Http;

/// <summary>
/// Receives rocket messages over HTTP on its own Kestrel server, separate from the query API
/// (see DEC-03). Rate limiting and backpressure apply only here.
/// </summary>
/// <remarks>The server is a minimal API app with a single <c>POST /messages</c> endpoint.</remarks>
internal sealed partial class HttpMessageListener(
    IMessageChannel channel,
    IOptions<HttpListenerOptions> listenerOptions,
    IOptions<RateLimitOptions> rateLimitOptions,
    ILoggerFactory loggerFactory) : IMessageListener, IAsyncDisposable
{
    internal const string RateLimitPolicy = "messages";

    private readonly ILogger _logger = loggerFactory.CreateLogger<HttpMessageListener>();
    private WebApplication? _app;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var url = listenerOptions.Value.Url;
        _app = CreateApp(webHost => webHost.UseUrls(url));
        await _app.StartAsync(cancellationToken);
        LogListening(url);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_app is not null)
        {
            await _app.StopAsync(cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    /// <summary>Builds the listener's web app. Tests use this to run it on a test server.</summary>
    internal WebApplication CreateApp(Action<IWebHostBuilder> configureWebHost)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(HttpMessageListener).Assembly.GetName().Name,
        });
        configureWebHost(builder.WebHost);

        // Share the host's logging and channel instead of creating new ones.
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(loggerFactory);
        builder.Services.AddSingleton(channel);
        builder.Services.AddSingleton(listenerOptions);
        builder.Services.AddSingleton<IHostLifetime, EmbeddedHostLifetime>();

        // Swagger's routes use the regex constraint, which the slim builder leaves out.
        builder.Services.Configure<RouteOptions>(routes => routes.SetParameterPolicy<RegexInlineRouteConstraint>("regex"));

        AddRateLimiting(builder.Services, rateLimitOptions.Value);

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(swagger =>
        {
            swagger.SwaggerDoc("v1", new OpenApiInfo { Title = "Rockets Message Listener", Version = "v1" });
            var xml = Path.Combine(AppContext.BaseDirectory, $"{typeof(HttpMessageListener).Assembly.GetName().Name}.xml");
            if (File.Exists(xml))
            {
                swagger.IncludeXmlComments(xml);
            }
        });

        var app = builder.Build();
        app.UseSwagger();
        app.UseSwaggerUI();
        app.UseRateLimiter();
        app.MapPost("/messages", ReceiveAsync)
            .RequireRateLimiting(RateLimitPolicy)
            .WithSummary("Receives a rocket message.")
            .WithDescription(
                "202 is returned only once the message is queued for processing. " +
                "Any other status makes the sender redeliver the message later.")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).ExcludeFromDescription();
        return app;
    }

    // Handles POST /messages. (A plain comment: an XML summary would replace the Swagger summary above.)
    private async Task<IResult> ReceiveAsync(MessageEnvelope envelope, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var result = RocketMessageMapper.Map(envelope);
        switch (result.Status)
        {
            case MapStatus.Invalid:
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, detail: result.Error);

            case MapStatus.UnknownType:
                // Acknowledged so the sender does not redeliver a message we will never understand.
                LogUnknownType(envelope.Metadata!.MessageType!, envelope.Metadata.Channel!, envelope.Metadata.MessageNumber);
                return Results.Accepted();
        }

        try
        {
            await channel.WriteAsync(result.Message!, cancellationToken);
            return Results.Accepted();
        }
        catch (MessageChannelUnavailableException ex)
        {
            // A full or closed channel: answer 503 with Retry-After so the sender redelivers.
            LogUnavailable(ex.Message);
            httpContext.Response.Headers.RetryAfter = listenerOptions.Value.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Service unavailable",
                detail: ex.Message);
        }
    }

    private void AddRateLimiting(IServiceCollection services, RateLimitOptions options) =>
        services.AddRateLimiter(limiter =>
        {
            // One bucket for all senders: it protects our own processing capacity (see DEC-05).
            limiter.AddTokenBucketLimiter(RateLimitPolicy, bucket =>
            {
                bucket.TokenLimit = options.TokenLimit;
                bucket.TokensPerPeriod = options.TokensPerPeriod;
                bucket.ReplenishmentPeriod = options.ReplenishmentPeriod;
                bucket.QueueLimit = 0;
                bucket.AutoReplenishment = true;
            });
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = (context, _) =>
            {
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait)
                    ? (int)Math.Ceiling(wait.TotalSeconds)
                    : listenerOptions.Value.RetryAfterSeconds;
                context.HttpContext.Response.Headers.RetryAfter = Math.Max(1, retryAfter).ToString(CultureInfo.InvariantCulture);
                LogRateLimited();
                return ValueTask.CompletedTask;
            };
        });

    [LoggerMessage(Level = LogLevel.Information, Message = "Message listener accepting messages on {Url}")]
    private partial void LogListening(string url);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Rejected message with 429: rate limit exceeded")]
    private partial void LogRateLimited();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected message with 503: {Reason}")]
    private partial void LogUnavailable(string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignored unknown message type {MessageType} (#{MessageNumber} on {Channel})")]
    private partial void LogUnknownType(string messageType, string channel, long messageNumber);
}
