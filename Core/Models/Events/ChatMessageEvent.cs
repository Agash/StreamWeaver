using StreamWeaver.Core.Models.Events.Messages;

namespace StreamWeaver.Core.Models.Events;

public class ChatMessageEvent : BaseEvent
{
    public string Username { get; init; } = string.Empty;

    /// <summary>
    /// The original raw message text received from the platform.
    /// </summary>
    public string? RawMessage { get; init; } // Made nullable if parsing fails? Keep required for now.

    /// <summary>
    /// The message content parsed into text and emote segments for rich rendering.
    /// </summary>
    public List<MessageSegment> ParsedMessage { get; init; } = [];

    public string? UserId { get; init; }

    /// <summary>
    /// Gets or sets the suggested color for the username based on badges (hex code or name).
    /// </summary>
    public string? UsernameColor { get; set; }

    /// <summary>
    /// Gets the list of badges associated with the user.
    /// </summary>
    public List<BadgeInfo> Badges { get; init; } = [];

    public string? ProfileImageUrl { get; set; }
    public bool IsOwner { get; set; }

    public bool IsActionMessage { get; init; } = false; // e.g., /me command
    public bool IsHighlight { get; init; } = false; // e.g., Channel points highlight
    public int BitsDonated { get; init; } = 0; // Bits included with the message

    /// <summary>
    /// Gets the plain text representation of the message by concatenating text segments.
    /// Useful for logging, TTS, or simple display scenarios.
    /// </summary>
    public string GetPlainText() => string.Join("", ParsedMessage.OfType<TextSegment>().Select(ts => ts.Text));
}
