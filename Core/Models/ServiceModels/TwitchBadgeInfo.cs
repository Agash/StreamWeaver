namespace StreamWeaver.Core.Models.ServiceModels;

// Represents a specific version of a Twitch badge within a set.
public record TwitchBadgeInfo(
    string ImageUrl1x,
    string ImageUrl2x,
    string ImageUrl4x,
    string Title, // e.g., "Subscriber", "Moderator"
    string? Description = null,
    string? ClickAction = null,
    string? ClickUrl = null
);

// Represents a set of badges (e.g., "subscriber" set containing multiple month versions).
// Keyed by Version ID (e.g., "0", "3", "6").
public partial class TwitchBadgeSet : Dictionary<string, TwitchBadgeInfo>
{
    public TwitchBadgeSet()
        : base(StringComparer.OrdinalIgnoreCase) { }
}
