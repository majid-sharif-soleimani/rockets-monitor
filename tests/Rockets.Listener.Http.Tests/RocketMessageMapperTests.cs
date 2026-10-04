using System.Text.Json;
using Rockets.Domain.Messages;
using Rockets.Listener.Http.Parsing;

namespace Rockets.Listener.Http.Tests;

public class RocketMessageMapperTests
{
    private static MessageEnvelope Envelope(string messageType, string payload) => new()
    {
        Metadata = new MessageMetadata
        {
            Channel = "a",
            MessageNumber = 3,
            MessageTime = DateTimeOffset.UnixEpoch,
            MessageType = messageType,
        },
        Message = JsonDocument.Parse(payload).RootElement,
    };

    [Fact]
    public void Maps_every_known_message_type()
    {
        Assert.Equal(
            new RocketLaunched("a", 3, DateTimeOffset.UnixEpoch, "Falcon-9", 500, "ARTEMIS"),
            Map("RocketLaunched", """{ "type": "Falcon-9", "launchSpeed": 500, "mission": "ARTEMIS" }"""));
        Assert.Equal(
            new RocketSpeedIncreased("a", 3, DateTimeOffset.UnixEpoch, 3000),
            Map("RocketSpeedIncreased", """{ "by": 3000 }"""));
        Assert.Equal(
            new RocketSpeedDecreased("a", 3, DateTimeOffset.UnixEpoch, 2500),
            Map("RocketSpeedDecreased", """{ "by": 2500 }"""));
        Assert.Equal(
            new RocketExploded("a", 3, DateTimeOffset.UnixEpoch, "PRESSURE_VESSEL_FAILURE"),
            Map("RocketExploded", """{ "reason": "PRESSURE_VESSEL_FAILURE" }"""));
        Assert.Equal(
            new RocketMissionChanged("a", 3, DateTimeOffset.UnixEpoch, "SHUTTLE_MIR"),
            Map("RocketMissionChanged", """{ "newMission": "SHUTTLE_MIR" }"""));
    }

    [Fact]
    public void Unknown_type_is_reported()
    {
        var result = RocketMessageMapper.Map(Envelope("RocketLanded", "{}"));

        Assert.Equal(MapStatus.UnknownType, result.Status);
    }

    [Fact]
    public void Missing_payload_field_is_invalid()
    {
        var result = RocketMessageMapper.Map(Envelope("RocketExploded", "{}"));

        Assert.Equal(MapStatus.Invalid, result.Status);
        Assert.NotNull(result.Error);
    }

    private static RocketMessage? Map(string type, string payload)
    {
        var result = RocketMessageMapper.Map(Envelope(type, payload));
        Assert.Equal(MapStatus.Mapped, result.Status);
        return result.Message;
    }
}
