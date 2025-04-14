using StreamWeaver.Core.Models.Events.Messages;

namespace StreamWeaver.Core.Models.Events;

/// <summary>
/// Represents the type of donation or monetary event.
/// </summary>
public enum DonationType
{
    Streamlabs,
    SuperChat,
    Bits,
    SuperSticker,
    Other,
}

/// <summary>
/// Represents a donation event (Streamlabs, Bits, SuperChat, SuperSticker).
/// </summary>
public class DonationEvent : BaseEvent
{
    public string Username { get; init; } = string.Empty;
    public string? UserId { get; init; }

    /// <summary>
    /// Gets or sets the suggested color for the username based on badges (hex code or name).
    /// </summary>
    public string? UsernameColor { get; set; }

    /// <summary>
    /// Gets the list of badges associated with the user.
    /// </summary>
    public List<BadgeInfo> Badges { get; init; } = []; // Changed type
    public decimal Amount { get; init; } = 0; // Monetary amount or Bit count
    public string Currency { get; init; } = "USD"; // Currency code (USD, EUR, etc.) or "Bits"

    /// <summary>
    /// The original raw message text received from the platform (user comment).
    /// </summary>
    public string RawMessage { get; init; } = string.Empty; // Ensure Required/Init if always present

    /// <summary>
    /// The message content parsed into text and emote segments for rich rendering.
    /// </summary>
    public List<MessageSegment> ParsedMessage { get; init; } = [];
    public string? ProfileImageUrl { get; set; }
    public bool IsOwner { get; set; }

    /// <summary>
    /// The type of donation event.
    /// </summary>
    public DonationType Type { get; init; } = DonationType.Other;

    /// <summary>
    /// A unique identifier for the donation or message ID associated with it.
    /// </summary>
    public string? DonationId { get; init; }

    // --- YouTube Specific Fields ---

    /// <summary>
    /// Hex color code (e.g., "1565C0") for the main background of a Super Chat or Super Sticker. Null if not applicable.
    /// </summary>
    public string? BodyBackgroundColor { get; set; }

    /// <summary>
    /// Hex color code for the header background of a Super Chat. Null for Super Stickers or if not applicable.
    /// </summary>
    public string? HeaderBackgroundColor { get; set; }

    /// <summary>
    /// Hex color code for the header text of a Super Chat. Null for Super Stickers or if not applicable.
    /// </summary>
    public string? HeaderTextColor { get; set; }

    /// <summary>
    /// Hex color code for the body text (user comment) of a Super Chat. Null for Super Stickers or if not applicable.
    /// </summary>
    public string? BodyTextColor { get; set; }

    /// <summary>
    /// Hex color code for the author's name within the Super Chat or Super Sticker. Null if not applicable.
    /// </summary>
    public string? AuthorNameTextColor { get; set; }

    /// <summary>
    /// URL of the image for a Super Sticker. Null if not a Super Sticker or URL unavailable.
    /// </summary>
    public string? StickerImageUrl { get; set; }

    /// <summary>
    /// Alt text or description for a Super Sticker. Null if not a Super Sticker or unavailable.
    /// </summary>
    public string? StickerAltText { get; set; }

    public string FormattedAmount
    {
        get
        {
            if (Type == DonationType.Bits)
            {
                return $"{Amount:N0} {(Amount == 1 ? "bit" : "bits")}";
            }
            else
            {
                try
                {
                    return $"{Amount:N2} {Currency}";
                }
                catch
                {
                    return $"{Amount:N2} {Currency}";
                }
            }
        }
    }
}
