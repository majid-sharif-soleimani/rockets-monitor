using System.Text.Json;
using Rockets.Domain.Messages;

namespace Rockets.Listener.Http.Parsing;

public enum MapStatus
{
    Mapped,
    UnknownType,
    Invalid,
}

public readonly record struct MapResult(MapStatus Status, RocketMessage? Message = null, string? Error = null);

/// <summary>Turns a validated envelope into a domain message based on its message type.</summary>
public static class RocketMessageMapper
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    public static MapResult Map(MessageEnvelope envelope)
    {
        var metadata = envelope.Metadata ?? throw new ArgumentException("Envelope has no metadata.", nameof(envelope));
        var channel = metadata.Channel!;
        var number = metadata.MessageNumber;
        var time = metadata.MessageTime!.Value;

        if (envelope.Message.ValueKind != JsonValueKind.Object)
        {
            return new MapResult(MapStatus.Invalid, Error: "'message' must be a JSON object.");
        }

        try
        {
            RocketMessage? message = metadata.MessageType switch
            {
                "RocketLaunched" => Read<LaunchedPayload>(envelope) switch
                {
                    var p => new RocketLaunched(channel, number, time, p.Type, p.LaunchSpeed, p.Mission),
                },
                "RocketSpeedIncreased" => new RocketSpeedIncreased(channel, number, time, Read<SpeedPayload>(envelope).By),
                "RocketSpeedDecreased" => new RocketSpeedDecreased(channel, number, time, Read<SpeedPayload>(envelope).By),
                "RocketExploded" => new RocketExploded(channel, number, time, Read<ExplodedPayload>(envelope).Reason),
                "RocketMissionChanged" => new RocketMissionChanged(channel, number, time, Read<MissionChangedPayload>(envelope).NewMission),
                _ => null,
            };

            return message is null
                ? new MapResult(MapStatus.UnknownType)
                : new MapResult(MapStatus.Mapped, message);
        }
        catch (JsonException ex)
        {
            return new MapResult(MapStatus.Invalid, Error: $"Invalid '{metadata.MessageType}' message: {ex.Message}");
        }
    }

    private static T Read<T>(MessageEnvelope envelope) =>
        envelope.Message.Deserialize<T>(PayloadOptions) ?? throw new JsonException("'message' is null.");

    private sealed record LaunchedPayload(string Type, long LaunchSpeed, string Mission);

    private sealed record SpeedPayload(long By);

    private sealed record ExplodedPayload(string Reason);

    private sealed record MissionChangedPayload(string NewMission);
}
