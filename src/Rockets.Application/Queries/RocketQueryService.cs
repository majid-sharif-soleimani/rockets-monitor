using Rockets.Domain.Rockets;

namespace Rockets.Application.Queries;

/// <summary>Reports on a single rocket.</summary>
public interface IRocketQueryService
{
    RocketDto? GetRocket(string channel);
}

public sealed class RocketQueryService(IRocketRegistry registry) : IRocketQueryService
{
    public RocketDto? GetRocket(string channel) =>
        registry.Find(channel) is { } monitor ? RocketDto.From(monitor.Current) : null;
}
