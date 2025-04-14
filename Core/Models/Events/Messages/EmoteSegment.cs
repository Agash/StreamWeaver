namespace StreamWeaver.Core.Models.Events.Messages;

/// <summary>
/// Represents an emote within a message.
/// </summary>
public class EmoteSegment : MessageSegment
{
    /// <summary>
    /// The textual code of the emote (e.g., "Kappa").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The URL for the default size (1x) image of the emote.
    /// </summary>
    public required string ImageUrl { get; init; }

    /// <summary>
    /// The platform-specific ID of the emote.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The platform identifier (e.g., "Twitch", "YouTube").
    /// </summary>
    public required string Platform { get; init; }

    public override string ToString() => Name;
}
