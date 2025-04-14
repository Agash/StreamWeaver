using StreamWeaver.Core.Models.Events.Messages;

namespace StreamWeaver.Core.Models.Events;

/// <summary>
/// Represents the type of YouTube membership event.
/// </summary>
public enum MembershipEventType
{
    Unknown,
    New,
    Milestone,
    GiftPurchase, // User announced they bought gifts
    GiftRedemption, // User received a gift
}

/// <summary>
/// Represents a YouTube Membership event (New, Milestone, Gifted).
/// </summary>
public class MembershipEvent : BaseEvent
{
    public string Username { get; init; } = string.Empty; // For New/Milestone/GiftRedemption: The member. For GiftPurchase: The Gifter.
    public string? UserId { get; init; }

    /// <summary>
    /// Gets or sets the suggested color for the username based on badges (hex code or name).
    /// </summary>
    public string? UsernameColor { get; set; }

    /// <summary>
    /// Gets the list of badges associated with the user (member or gifter).
    /// </summary>
    public List<BadgeInfo> Badges { get; init; } = [];
    public string? ProfileImageUrl { get; set; }
    public bool IsOwner { get; set; }

    /// <summary>
    /// The type of membership event.
    /// </summary>
    public MembershipEventType MembershipType { get; init; } = MembershipEventType.Unknown;

    /// <summary>
    /// The user-visible name of the membership level or tier.
    /// </summary>
    public string? LevelName { get; init; } = "Member"; // Renamed from Tier

    /// <summary>
    /// The number of months for a Milestone event. Null for other types.
    /// </summary>
    public int? MilestoneMonths { get; init; }

    /// <summary>
    /// The username of the user who gifted the membership(s).
    /// Applicable only when MembershipType is GiftPurchase. Null otherwise.
    /// </summary>
    public string? GifterUsername { get; init; }

    /// <summary>
    /// The number of memberships gifted in this event.
    /// Applicable only when MembershipType is GiftPurchase. Null otherwise.
    /// </summary>
    public int? GiftCount { get; init; }

    /// <summary>
    /// The username of the user who received a gifted membership.
    /// Currently derived from the main Username property when MembershipType is GiftRedemption.
    /// </summary>
    // public string? RecipientUsername { get; init; } // Potentially redundant with Username for GiftRedemption

    /// <summary>
    /// The primary system message text associated with the event (e.g., "Member for 6 months", "Welcome!").
    /// Stored here instead of RawMessage.
    /// </summary>
    public string? HeaderText { get; init; } // Replaces RawMessage for membership system text

    /// <summary>
    /// The parsed user comment, if any (typically only for Milestone events).
    /// </summary>
    public List<MessageSegment> ParsedMessage { get; init; } = []; // User comment parsed
}
