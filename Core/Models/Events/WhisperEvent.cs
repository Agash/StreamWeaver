using StreamWeaver.Core.Models.Events.Messages;

namespace StreamWeaver.Core.Models.Events;

public class WhisperEvent : BaseEvent
{
    public string Username { get; set; } = string.Empty;
    public string? UserId { get; set; }

    /// <summary>
    /// Gets or sets the suggested color for the username based on badges (hex code or name).
    /// </summary>
    public string? UserColor { get; set; }

    /// <summary>
    /// Gets the list of badges associated with the user.
    /// </summary>
    public List<BadgeInfo> Badges { get; init; } = [];
    public string Message { get; set; } = string.Empty;
    public string? ProfileImageUrl { get; set; }

    public List<MessageSegment> ParsedMessage { get; init; } = [];
}
