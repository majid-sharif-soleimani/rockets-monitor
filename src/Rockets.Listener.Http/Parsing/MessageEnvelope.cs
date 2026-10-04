using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Rockets.Listener.Http.Parsing;

/// <summary>
/// A message as posted by a rocket. The payload's shape depends on
/// <see cref="MessageMetadata.MessageType"/>, which sits next to it rather than inside it,
/// so the payload is kept as raw JSON and mapped by <see cref="RocketMessageMapper"/>.
/// </summary>
public sealed class MessageEnvelope
{
    [Required]
    public MessageMetadata? Metadata { get; init; }

    public JsonElement Message { get; init; }
}

public sealed class MessageMetadata
{
    /// <summary>The rocket's radio channel, which identifies the rocket.</summary>
    [Required(AllowEmptyStrings = false)]
    public string? Channel { get; init; }

    /// <summary>The message's position within the channel, starting at 1.</summary>
    [Range(1, long.MaxValue)]
    public long MessageNumber { get; init; }

    [Required]
    public DateTimeOffset? MessageTime { get; init; }

    /// <example>RocketLaunched</example>
    [Required(AllowEmptyStrings = false)]
    public string? MessageType { get; init; }
}
