using StreamWeaver.Core.Models.Events.Messages;

namespace StreamWeaver.Core.Services;

/// <summary>
/// Service responsible for fetching, caching, and providing access to
/// chat badges and emotes for various platforms.
/// </summary>
public interface IEmoteBadgeService
{
    /// <summary>
    /// Loads global Twitch badges and emotes into the cache.
    /// Should be called on application startup or periodically.
    /// </summary>
    Task LoadGlobalTwitchDataAsync();

    /// <summary>
    /// Loads channel-specific Twitch badges and emotes into the cache for a given channel.
    /// </summary>
    /// <param name="channelId">The Twitch User ID of the channel whose data is needed.</param>
    /// <param name="userAccountId">The Twitch User ID of the *authenticated user* making the request (for API context).</param>
    Task LoadChannelTwitchDataAsync(string channelId, string userAccountId);

    /// <summary>
    /// Attempts to retrieve the URL for a specific Twitch badge.
    /// </summary>
    /// <param name="badgeSetId">The badge set ID (e.g., "subscriber", "moderator").</param>
    /// <param name="badgeVersionId">The specific version ID of the badge (e.g., "0", "1", "1000").</param>
    /// <returns>The URL string if found in cache, otherwise null.</returns>
    string? GetTwitchBadgeUrl(string badgeSetId, string badgeVersionId);

    /// <summary>
    /// Parses a raw YouTube message string (potentially containing HTML)
    /// into a list of segments (text, emotes/images, emojis).
    /// </summary>
    /// <param name="rawMessage">The raw message text, possibly with HTML img tags.</param>
    /// <returns>A list of MessageSegment objects.</returns>
    List<MessageSegment> ParseYouTubeMessage(string rawMessage);

    /// <summary>
    /// Retrieves information about a YouTube Super Sticker, potentially caching its URL.
    /// </summary>
    /// <param name="stickerId">Identifier for the sticker.</param>
    /// <returns>URL or metadata object if found/cached.</returns>
    Task<string?> GetYouTubeStickerUrlAsync(string stickerId);
}
