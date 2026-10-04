using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Rockets.Application.Messaging;
using Rockets.Listener.Http.Controllers;

namespace Rockets.Listener.Http;

/// <summary>
/// Receives rocket messages over HTTP on its own Kestrel server, separate from the query API
/// (see DEC-03). Rate limiting and backpressure apply only here.
/// </summary>
public sealed partial class HttpMessageListener(
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

        builder.Services
            .AddControllers(mvc => mvc.Filters.Add<ChannelUnavailableExceptionFilter>())
            .ConfigureApplicationPartManager(parts =>
            {
                // Only this assembly's controllers: the query API's controllers must not be exposed here.
                parts.ApplicationParts.Clear();
                parts.ApplicationParts.Add(new AssemblyPart(typeof(MessagesController).Assembly));
            });

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
        app.MapControllers();
        app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).ExcludeFromDescription();
        return app;
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
}
