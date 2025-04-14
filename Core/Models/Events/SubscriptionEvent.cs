namespace StreamWeaver.Core.Models.Events;

// Note: This class primarily represents Twitch subscriptions now.
// YouTube Memberships are handled by MembershipEvent.
public class SubscriptionEvent : BaseEvent
{
    public string Username { get; init; } = string.Empty; // User subscribing or gifting
    public string? UserId { get; init; }

    /// <summary>
    /// Gets or sets the suggested color for the username based on badges (hex code or name).
    /// </summary>
    public string? UsernameColor { get; set; }

    /// <summary>
    /// Gets the list of badges associated with the user (subscriber or gifter).
    /// </summary>
    public List<BadgeInfo> Badges { get; init; } = []; // Changed type
    public string? ProfileImageUrl { get; set; }
    public bool IsOwner { get; set; }

    public bool IsGift { get; init; } = false;
    public string? RecipientUsername { get; init; } // If IsGift = true
    public string? RecipientUserId { get; init; } // If IsGift = true
    public int Months { get; init; } = 1; // Duration of this sub event (or cumulative for resubs on some platforms)
    public int CumulativeMonths { get; init; } = 0; // User's total months (if known)
    public int GiftCount { get; init; } = 1; // Number of gifts purchased in this single event
    public int TotalGiftCount { get; init; } = 0; // Gifter's total gifts in channel (if known)
    public string Tier { get; init; } = "Tier 1"; // e.g., "Tier 1", "Tier 2", "Twitch Prime"
    public string? Message { get; init; } // Optional message with resub/gift
}
