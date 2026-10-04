using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rockets.Application.Messaging;
using Rockets.Domain.Messages;
using Rockets.Domain.Rockets;

namespace Rockets.Api.IntegrationTests;

public sealed class RocketsApiTests : IClassFixture<RocketsApiTests.ApiFactory>
{
    private static readonly DateTimeOffset T0 = new(2022, 2, 2, 19, 39, 5, TimeSpan.Zero);

    private readonly HttpClient _client;

    public RocketsApiTests(ApiFactory factory)
    {
        _client = factory.CreateClient();

        var registry = factory.Services.GetRequiredService<IRocketRegistry>();
        registry.GetOrCreate("alpha").Apply(new RocketLaunched("alpha", 1, T0, "Falcon-9", 500, "ARTEMIS"));
        registry.GetOrCreate("alpha").Apply(new RocketSpeedIncreased("alpha", 2, T0.AddSeconds(1), 3000));
        registry.GetOrCreate("bravo").Apply(new RocketLaunched("bravo", 1, T0, "Saturn-V", 9000, "APOLLO"));
        registry.GetOrCreate("bravo").Apply(new RocketExploded("bravo", 2, T0, "PRESSURE_VESSEL_FAILURE"));
        registry.GetOrCreate("charlie").Apply(new RocketSpeedDecreased("charlie", 3, T0, 10));
    }

    [Fact]
    public async Task Get_rocket_returns_its_current_state()
    {
        var rocket = await _client.GetFromJsonAsync<JsonElement>("/api/rockets/alpha");

        Assert.Equal("alpha", rocket.GetProperty("channel").GetString());
        Assert.True(rocket.GetProperty("launched").GetBoolean());
        Assert.Equal("Falcon-9", rocket.GetProperty("type").GetString());
        Assert.Equal(3500, rocket.GetProperty("speed").GetInt64());
        Assert.Equal("ARTEMIS", rocket.GetProperty("mission").GetString());
        Assert.Equal("active", rocket.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Get_unknown_rocket_returns_404()
    {
        var response = await _client.GetAsync("/api/rockets/unknown");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Fleet_list_is_sorted_and_paged()
    {
        var page = await _client.GetFromJsonAsync<JsonElement>("/api/fleet/rockets?sortBy=speed&order=desc&pageSize=2");

        var channels = page.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("channel").GetString());
        Assert.Equal(["bravo", "alpha"], channels);
        Assert.Equal(3, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, page.GetProperty("totalPages").GetInt32());
    }

    [Fact]
    public async Task Fleet_list_filters_by_status()
    {
        var page = await _client.GetFromJsonAsync<JsonElement>("/api/fleet/rockets?status=notLaunched");

        var rocket = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal("charlie", rocket.GetProperty("channel").GetString());
        Assert.False(rocket.GetProperty("launched").GetBoolean());
    }

    [Theory]
    [InlineData("/api/fleet/rockets?sortBy=altitude")]
    [InlineData("/api/fleet/rockets?pageSize=0")]
    [InlineData("/api/fleet/rockets?pageSize=501")]
    public async Task Invalid_fleet_query_returns_400(string url)
    {
        var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Fleet_summary_counts_rockets()
    {
        var summary = await _client.GetFromJsonAsync<JsonElement>("/api/fleet/summary");

        Assert.Equal(3, summary.GetProperty("total").GetInt32());
        Assert.Equal(1, summary.GetProperty("active").GetInt32());
        Assert.Equal(1, summary.GetProperty("exploded").GetInt32());
        Assert.Equal(1, summary.GetProperty("notLaunched").GetInt32());
        Assert.Equal(1, summary.GetProperty("byType").GetProperty("Falcon-9").GetInt32());
    }

    [Fact]
    public async Task Message_endpoint_is_not_exposed_on_the_api_port()
    {
        var response = await _client.PostAsJsonAsync("/messages", new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public sealed class ApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            // The HTTP listener binds a real port; it is tested separately.
            builder.ConfigureServices(services => services.RemoveAll<IMessageListener>());
    }
}
