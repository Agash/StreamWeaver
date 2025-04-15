using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using StreamWeaver.Core.Models.Events.Messages;
using StreamWeaver.Core.Models.ServiceModels;
using StreamWeaver.Core.Services.Platforms;

namespace StreamWeaver.Core.Services;

/// <summary>
/// Provides services for loading, caching, and accessing emote and badge information
/// from various streaming platforms like Twitch and YouTube.
/// </summary>
public partial class EmoteBadgeService : IEmoteBadgeService
{
    private readonly TwitchApiService _twitchApiService;
    private readonly ILogger<EmoteBadgeService> _logger;

    /// <summary>
    /// Cache for global Twitch badges (e.g., prime, turbo). These rarely change.
    /// Key: Badge Set ID (e.g., "subscriber", "moderator")
    /// Value: Dictionary mapping Version ID (e.g., "0", "3") to Badge Info.
    /// </summary>
    private readonly ConcurrentDictionary<string, TwitchBadgeSet> _globalTwitchBadges = new();

    /// <summary>
    /// Cache for channel-specific Twitch badges (e.g., subscriber badges). These expire.
    /// Key: Channel ID (Twitch User ID of the channel)
    /// Value: Cached data containing the timestamp and the channel's badge sets.
    /// </summary>
    private readonly ConcurrentDictionary<string, CachedChannelData<TwitchBadgeSet>> _channelTwitchBadges = new();

    /// <summary>
    /// Defines how long channel-specific data (badges) remains valid in the cache.
    /// </summary>
    private static readonly TimeSpan s_channelDataCacheDuration = TimeSpan.FromHours(4);

    /// <summary>
    /// Helper record for storing cached data along with the timestamp it was fetched.
    /// </summary>
    /// <typeparam name="T">The type of data being cached (e.g., ConcurrentDictionary).</typeparam>
    /// <param name="Timestamp">The UTC time when the data was fetched.</param>
    /// <param name="Data">The actual cached data.</param>
    private record CachedChannelData<T>(DateTimeOffset Timestamp, ConcurrentDictionary<string, T> Data);

    /// <summary>
    /// Initializes a new instance of the <see cref="EmoteBadgeService"/> class.
    /// </summary>
    /// <param name="twitchApiService">The service for interacting with the Twitch API.</param>
    /// <param name="logger">The logger instance.</param>
    /// <exception cref="ArgumentNullException">Thrown if twitchApiService or logger is null.</exception>
    public EmoteBadgeService(TwitchApiService twitchApiService, ILogger<EmoteBadgeService> logger)
    {
        _twitchApiService = twitchApiService ?? throw new ArgumentNullException(nameof(twitchApiService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogInformation("Initialized.");
    }

    // --- Twitch Data Loading ---

    /// <summary>
    /// Loads global Twitch data (currently only badges) into the cache.
    /// This should typically be called once on application startup or periodically.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task LoadGlobalTwitchDataAsync()
    {
        _logger.LogInformation("Loading global Twitch data...");
        await LoadGlobalTwitchBadgesAsync();
        _logger.LogInformation("Global data load complete. Global badge sets cached: {BadgeCount}", _globalTwitchBadges.Count);
    }

    /// <summary>
    /// Fetches and caches global Twitch chat badges from the Twitch API.
    /// </summary>
    private async Task LoadGlobalTwitchBadgesAsync()
    {
        try
        {
            TwitchLib.Api.Helix.Models.Chat.Badges.GetGlobalChatBadges.GetGlobalChatBadgesResponse? response =
                await _twitchApiService.GetGlobalBadgesAsync();
            if (response?.EmoteSet == null)
            {
                _logger.LogWarning("Received null or empty response when fetching global Twitch badges.");
                return;
            }

            _globalTwitchBadges.Clear();
            int loadedSets = 0;
            foreach (TwitchLib.Api.Helix.Models.Chat.Badges.BadgeEmoteSet? badgeSet in response.EmoteSet)
            {
                if (string.IsNullOrEmpty(badgeSet?.SetId) || badgeSet.Versions == null)
                    continue;

                TwitchBadgeSet versions = [];
                foreach (TwitchLib.Api.Helix.Models.Chat.Badges.BadgeVersion? version in badgeSet.Versions)
                {
                    if (
                        string.IsNullOrEmpty(version?.Id)
                        || string.IsNullOrEmpty(version.ImageUrl1x)
                        || string.IsNullOrEmpty(version.ImageUrl2x)
                        || string.IsNullOrEmpty(version.ImageUrl4x)
                    )
                    {
                        continue;
                    }

                    versions[version.Id] = new TwitchBadgeInfo(version.ImageUrl1x, version.ImageUrl2x, version.ImageUrl4x, badgeSet.SetId);
                }

                if (versions.Count > 0)
                {
                    _globalTwitchBadges[badgeSet.SetId] = versions;
                    loadedSets++;
                }
            }

            _logger.LogDebug("Successfully processed {LoadedSets} global Twitch badge sets.", loadedSets);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading global Twitch badges: {ErrorMessage}", ex.Message);
            // Consider clearing the cache on error? Or keep potentially stale data?
            // _globalTwitchBadges.Clear();
        }
    }

    /// <summary>
    /// Loads channel-specific Twitch data (currently only badges) for a given channel ID.
    /// Ensures the data is cached with an expiration time.
    /// </summary>
    /// <param name="channelId">The Twitch User ID of the channel.</param>
    /// <param name="userAccountId">The Twitch User ID of the authenticated user making the request (required by API).</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task LoadChannelTwitchDataAsync(string channelId, string userAccountId)
    {
        if (string.IsNullOrEmpty(channelId) || string.IsNullOrEmpty(userAccountId))
        {
            _logger.LogWarning(
                "LoadChannelTwitchDataAsync called with null or empty channelId ({ChannelId}) or userAccountId ({UserAccountId}).",
                channelId,
                userAccountId
            );
            return;
        }

        if (_channelTwitchBadges.TryGetValue(channelId, out CachedChannelData<TwitchBadgeSet>? existingCache) && IsCacheEntryValid(existingCache))
        {
            _logger.LogDebug("Channel Twitch data for {ChannelId} is already cached and valid.", channelId);
            return;
        }

        _logger.LogInformation(
            "Loading channel Twitch data for Channel ID: {ChannelId} (requested by User ID: {UserAccountId})...",
            channelId,
            userAccountId
        );

        await LoadChannelTwitchBadgesAsync(channelId, userAccountId);
        _logger.LogInformation("Channel data load attempt complete for Channel ID: {ChannelId}.", channelId);
    }

    /// <summary>
    /// Fetches and caches channel-specific Twitch chat badges from the Twitch API.
    /// </summary>
    private async Task LoadChannelTwitchBadgesAsync(string channelId, string userAccountId)
    {
        try
        {
            TwitchLib.Api.Helix.Models.Chat.Badges.GetChannelChatBadges.GetChannelChatBadgesResponse? response =
                await _twitchApiService.GetChannelBadgesAsync(channelId, userAccountId);
            if (response?.EmoteSet == null)
            {
                _logger.LogWarning("Received null or empty response when fetching channel badges for Channel ID {ChannelId}.", channelId);
                _channelTwitchBadges[channelId] = new CachedChannelData<TwitchBadgeSet>(
                    DateTimeOffset.UtcNow,
                    new ConcurrentDictionary<string, TwitchBadgeSet>()
                );
                return;
            }

            ConcurrentDictionary<string, TwitchBadgeSet> channelCache = new();
            int loadedSets = 0;
            foreach (TwitchLib.Api.Helix.Models.Chat.Badges.BadgeEmoteSet? badgeSet in response.EmoteSet)
            {
                if (string.IsNullOrEmpty(badgeSet?.SetId) || badgeSet.Versions == null)
                    continue;

                TwitchBadgeSet versions = [];
                foreach (TwitchLib.Api.Helix.Models.Chat.Badges.BadgeVersion? version in badgeSet.Versions)
                {
                    if (
                        string.IsNullOrEmpty(version?.Id)
                        || string.IsNullOrEmpty(version.ImageUrl1x)
                        || string.IsNullOrEmpty(version.ImageUrl2x)
                        || string.IsNullOrEmpty(version.ImageUrl4x)
                    )
                    {
                        continue;
                    }

                    versions[version.Id] = new TwitchBadgeInfo(version.ImageUrl1x, version.ImageUrl2x, version.ImageUrl4x, badgeSet.SetId);
                }

                if (versions.Count > 0)
                {
                    channelCache[badgeSet.SetId] = versions;
                    loadedSets++;
                }
            }

            _channelTwitchBadges[channelId] = new CachedChannelData<TwitchBadgeSet>(DateTimeOffset.UtcNow, channelCache);
            _logger.LogInformation("Cached {BadgeSetCount} Twitch badge sets for Channel ID {ChannelId}.", loadedSets, channelId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading channel Twitch badges for Channel ID {ChannelId}: {ErrorMessage}", channelId, ex.Message);
            _channelTwitchBadges.TryRemove(channelId, out _);
        }
    }

    // --- Twitch Data Access ---

    /// <summary>
    /// Retrieves the URL for a specific Twitch badge version.
    /// Checks global cache first, then iterates through valid channel caches.
    /// </summary>
    /// <param name="badgeSetId">The ID of the badge set (e.g., "subscriber").</param>
    /// <param name="badgeVersionId">The specific version ID of the badge (e.g., "0", "3").</param>
    /// <returns>The URL (usually 1x resolution) of the badge image, or null if not found in caches.</returns>
    public string? GetTwitchBadgeUrl(string badgeSetId, string badgeVersionId)
    {
        if (
            _globalTwitchBadges.TryGetValue(badgeSetId, out TwitchBadgeSet? globalSet)
            && globalSet.TryGetValue(badgeVersionId, out TwitchBadgeInfo? globalBadge)
        )
        {
            _logger.LogTrace("Found badge {BadgeSetId}/{BadgeVersionId} in global cache.", badgeSetId, badgeVersionId);
            return globalBadge.ImageUrl1x;
        }

        // Check channel caches (less efficient as it iterates through all loaded channels)
        // Optimization: If channel context (channelId) is available where this is called,
        // pass it in and check that specific channel's cache first.
        foreach (KeyValuePair<string, CachedChannelData<TwitchBadgeSet>> kvp in _channelTwitchBadges)
        {
            string channelId = kvp.Key;
            CachedChannelData<TwitchBadgeSet> channelCacheEntry = kvp.Value;

            if (IsCacheEntryValid(channelCacheEntry))
            {
                if (
                    channelCacheEntry.Data.TryGetValue(badgeSetId, out TwitchBadgeSet? channelSet)
                    && channelSet.TryGetValue(badgeVersionId, out TwitchBadgeInfo? channelBadge)
                )
                {
                    _logger.LogTrace(
                        "Found badge {BadgeSetId}/{BadgeVersionId} in cache for channel {ChannelId}.",
                        badgeSetId,
                        badgeVersionId,
                        channelId
                    );
                    return channelBadge.ImageUrl1x;
                }
            }
            // Optionally log if cache was stale?
            // else { _logger.LogTrace("Cache for channel {ChannelId} is stale.", channelId); }
        }

        _logger.LogDebug("Badge {BadgeSetId}/{BadgeVersionId} not found in any valid cache.", badgeSetId, badgeVersionId);
        // TODO: Consider triggering an async load/refresh if a badge is requested but not found?
        // This could lead to complex state management.
        return null;
    }

    // --- YouTube Handling ---

    /// <summary>
    /// Parses a raw YouTube message string, potentially containing HTML for custom emojis,
    /// into a list of message segments suitable for rendering.
    /// Currently, standard Unicode emojis are expected to be rendered by the UI framework.
    /// Custom YouTube emoji (<img> tags) are not parsed yet.
    /// </summary>
    /// <param name="rawMessage">The raw message text from YouTube, possibly containing HTML.</param>
    /// <returns>A list of <see cref="MessageSegment"/> objects representing the parsed message.</returns>
    public List<MessageSegment> ParseYouTubeMessage(string rawMessage)
    {
        // Basic approach: Treat the entire message as a single text segment.
        // RichTextBlock (WinUI) or HTML rendering (Web) handles standard Unicode emojis.
        // Future Enhancement: Parse for <img> tags with specific classes (e.g., 'yt-emoji')
        // and create EmoteSegments if YouTube provides custom emoji this way in the future.
        // Need to inspect actual live chat data to confirm the format.

        if (string.IsNullOrEmpty(rawMessage))
        {
            _logger.LogTrace("ParseYouTubeMessage called with null or empty message.");
            return [];
        }

        // For now, assume no special parsing needed beyond what the UI framework handles.
        // HTML decoding might be necessary if the raw message contains entities like &
        // string decodedMessage = System.Net.WebUtility.HtmlDecode(rawMessage); // Consider if needed
        // Return a list containing a single TextSegment with the raw message.
        _logger.LogTrace("Parsing YouTube message as a single text segment.");
        return [new TextSegment { Text = rawMessage }];
    }

    /// <summary>
    /// Retrieves the URL for a YouTube Super Sticker.
    /// (Placeholder - Implementation needed).
    /// </summary>
    /// <param name="stickerId">The ID of the sticker.</param>
    /// <returns>A task resulting in the sticker image URL, or null if not found or not implemented.</returns>
    public Task<string?> GetYouTubeStickerUrlAsync(string stickerId)
    {
        // TODO: Implement logic to retrieve sticker URLs.
        // This might involve:
        // 1. Checking if the URL is directly provided in the SuperStickerEvent data.
        // 2. Calling a YouTube API endpoint if necessary (unlikely for stickers, usually included).
        // 3. Implementing caching if URLs are fetched and static.
        _logger.LogWarning("YouTube sticker URL retrieval is not implemented. Requested Sticker ID: {StickerId}", stickerId);
        return Task.FromResult<string?>(null); // Placeholder
    }

    // --- Cache Helpers ---

    /// <summary>
    /// Checks if a cached data entry is valid (not null and within the cache duration).
    /// </summary>
    /// <typeparam name="T">The type of data stored in the cache entry.</typeparam>
    /// <param name="entry">The cached data entry.</param>
    /// <returns>True if the entry is valid, false otherwise.</returns>
    private static bool IsCacheEntryValid<T>(CachedChannelData<T>? entry) =>
        entry != null && (DateTimeOffset.UtcNow - entry.Timestamp) < s_channelDataCacheDuration;
}
