using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.OpenApi;
using Rockets.Api.Endpoints;
using Rockets.Application;
using Rockets.Domain;
using Rockets.Infrastructure;
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

// Domain: creates the rocket monitors
builder.Services.AddDomainServices();

// Infrastructure: the in-memory rocket registry and message channel
builder.Services.AddInfrastructureServices();

// The HTTP listener, on its own port
builder.Services.AddHttpMessageListener();

// Application: report services, the consumer and the listener host
builder.Services.AddApplicationServices();

// Query API (minimal API endpoints, see the Endpoints folder)
var enumsAsText = new JsonStringEnumConverter(JsonNamingPolicy.CamelCase);
builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.Converters.Add(enumsAsText));
// Swagger reads the MVC JSON options to describe enums, even when no controllers are used.
builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(json => json.JsonSerializerOptions.Converters.Add(enumsAsText));

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
app.MapRocketEndpoints();
app.MapFleetEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).ExcludeFromDescription();

app.Run();

public partial class Program;
