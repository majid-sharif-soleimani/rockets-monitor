using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.OpenApi;
using Rockets.Application.Consumers;
using Rockets.Application.Messaging;
using Rockets.Application.Queries;
using Rockets.Domain.Rockets;
using Rockets.Infrastructure.Messaging;
using Rockets.Listener.Http;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services));

// Configuration
builder.Services.Configure<MessageChannelOptions>(builder.Configuration.GetSection(MessageChannelOptions.SectionName));
builder.Services.Configure<HttpListenerOptions>(builder.Configuration.GetSection(HttpListenerOptions.SectionName));
builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection(RateLimitOptions.SectionName));

// Domain and application
builder.Services.AddSingleton<IRocketRegistry, RocketRegistry>();
builder.Services.AddSingleton<IRocketQueryService, RocketQueryService>();
builder.Services.AddSingleton<IFleetReportService, FleetReportService>();

// Messaging. Hosted services stop in reverse order: the listeners stop accepting messages
// first, then the consumer completes the channel and drains it.
builder.Services.AddSingleton<IMessageChannel, InMemoryMessageChannel>();
builder.Services.AddSingleton<IMessageListener, HttpMessageListener>();
builder.Services.AddHostedService<RocketMessageConsumer>();
builder.Services.AddHostedService<MessageListenersHostedService>();

// Query API
builder.Services
    .AddControllers()
    .AddJsonOptions(json => json.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)))
    .ConfigureApplicationPartManager(parts =>
    {
        // The message endpoint is served only by the listener, on its own port.
        var listenerPart = parts.ApplicationParts.FirstOrDefault(p => p.Name == typeof(HttpMessageListener).Assembly.GetName().Name);
        if (listenerPart is not null)
        {
            parts.ApplicationParts.Remove(listenerPart);
        }
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(swagger =>
{
    swagger.SwaggerDoc("v1", new OpenApiInfo { Title = "Rockets Monitor API", Version = "v1" });
    swagger.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, $"{typeof(Program).Assembly.GetName().Name}.xml"));
});

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).ExcludeFromDescription();

app.Run();

public partial class Program;
