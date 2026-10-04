using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rockets.Application.Messaging;
using Rockets.Domain.Messages;

namespace Rockets.Listener.Http.Tests;

public sealed class HttpMessageListenerTests : IAsyncLifetime
{
    private const string LaunchedJson = """
        {
          "metadata": {
            "channel": "193270a9-c9cf-404a-8f83-838e71d9ae67",
            "messageNumber": 1,
            "messageTime": "2022-02-02T19:39:05.86337+01:00",
            "messageType": "RocketLaunched"
          },
          "message": { "type": "Falcon-9", "launchSpeed": 500, "mission": "ARTEMIS" }
        }
        """;

    private readonly FakeChannel _channel = new();
    private WebApplication? _app;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    private async Task<HttpClient> StartAsync(RateLimitOptions? rateLimit = null)
    {
        var listener = new HttpMessageListener(
            _channel,
            Options.Create(new HttpListenerOptions { RetryAfterSeconds = 2 }),
            Options.Create(rateLimit ?? new RateLimitOptions()),
            NullLoggerFactory.Instance);

        _app = listener.CreateApp(webHost => webHost.UseTestServer());
        await _app.StartAsync();
        return _app.GetTestClient();
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, string json) =>
        client.PostAsync("/messages", new StringContent(json, Encoding.UTF8, "application/json"));

    [Fact]
    public async Task Valid_message_is_written_to_the_channel_and_accepted()
    {
        var client = await StartAsync();

        var response = await Post(client, LaunchedJson);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var launched = Assert.IsType<RocketLaunched>(Assert.Single(_channel.Written));
        Assert.Equal("193270a9-c9cf-404a-8f83-838e71d9ae67", launched.Channel);
        Assert.Equal(1, launched.MessageNumber);
        Assert.Equal("Falcon-9", launched.Type);
        Assert.Equal(500, launched.LaunchSpeed);
        Assert.Equal("ARTEMIS", launched.Mission);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "message": { "by": 1 } }""")]
    [InlineData("""{ "metadata": { "channel": "a", "messageNumber": 0, "messageTime": "2022-02-02T19:39:05Z", "messageType": "RocketSpeedIncreased" }, "message": { "by": 1 } }""")]
    [InlineData("""{ "metadata": { "channel": "a", "messageNumber": 1, "messageTime": "2022-02-02T19:39:05Z", "messageType": "RocketSpeedIncreased" }, "message": { } }""")]
    public async Task Malformed_message_is_rejected_with_400(string json)
    {
        var client = await StartAsync();

        var response = await Post(client, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_channel.Written);
    }

    [Fact]
    public async Task Unknown_message_type_is_accepted_but_not_written()
    {
        var client = await StartAsync();

        var response = await Post(client, LaunchedJson.Replace("RocketLaunched", "RocketLanded"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(_channel.Written);
    }

    [Fact]
    public async Task Full_channel_returns_503_with_retry_after()
    {
        _channel.Full = true;
        var client = await StartAsync();

        var response = await Post(client, LaunchedJson);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(2), response.Headers.RetryAfter?.Delta);
    }

    [Fact]
    public async Task Exceeding_the_rate_limit_returns_429_with_retry_after()
    {
        var client = await StartAsync(new RateLimitOptions
        {
            TokenLimit = 1,
            TokensPerPeriod = 1,
            ReplenishmentPeriod = TimeSpan.FromHours(1),
        });

        var first = await Post(client, LaunchedJson);
        var second = await Post(client, LaunchedJson);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.NotNull(second.Headers.RetryAfter?.Delta);
    }

    private sealed class FakeChannel : IMessageChannel
    {
        public List<RocketMessage> Written { get; } = [];

        public bool Full { get; set; }

        public int Count => Written.Count;

        public ValueTask WriteAsync(RocketMessage message, CancellationToken cancellationToken)
        {
            if (Full)
            {
                throw new MessageChannelFullException("full");
            }

            Written.Add(message);
            return ValueTask.CompletedTask;
        }

        public IAsyncEnumerable<RocketMessage> ReadAllAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void Complete()
        {
        }
    }
}
