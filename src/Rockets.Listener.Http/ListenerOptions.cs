namespace Rockets.Listener.Http;

public sealed class HttpListenerOptions
{
    public const string SectionName = "Listener";

    /// <summary>Address the message listener binds to. Kept separate from the query API.</summary>
    public string Url { get; set; } = "http://0.0.0.0:8088";

    /// <summary><c>Retry-After</c> sent when the channel is full (503).</summary>
    public int RetryAfterSeconds { get; set; } = 1;
}

/// <summary>Global token bucket protecting message processing (429 when exhausted).</summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimit";

    public int TokenLimit { get; set; } = 20_000;

    public int TokensPerPeriod { get; set; } = 20_000;

    public TimeSpan ReplenishmentPeriod { get; set; } = TimeSpan.FromSeconds(1);
}
