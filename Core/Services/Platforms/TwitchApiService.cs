using Microsoft.Extensions.Logging;
using StreamWeaver.Core.Services.Authentication;
using StreamWeaver.Core.Services.Settings;
using TwitchLib.Api;
using TwitchLib.Api.Core.Exceptions;
using TwitchLib.Api.Helix.Models.Chat.Badges.GetChannelChatBadges;
using TwitchLib.Api.Helix.Models.Chat.Badges.GetGlobalChatBadges;
using TwitchLib.Api.Helix.Models.Chat.Emotes.GetChannelEmotes;
using TwitchLib.Api.Helix.Models.Chat.Emotes.GetGlobalEmotes;
using TwitchLib.Api.Helix.Models.Users.GetUsers;

namespace StreamWeaver.Core.Services.Platforms;

/// <summary>
/// Service facilitating interaction with the Twitch Helix API for fetching data like
/// user information, stream details, chat badges, and emotes. Handles API client initialization
/// and token management (prioritizing user tokens when available, falling back to app tokens).
/// </summary>
public class TwitchApiService
{
    private readonly ISettingsService _settingsService;
    private readonly ITokenStorageService _tokenStorage;
    private readonly ILogger<TwitchApiService> _logger;
    private TwitchAPI? _api;

    private static string? s_appAccessToken;
    private static DateTimeOffset s_appAccessTokenExpiry;

    /// <summary>
    /// Initializes a new instance of the <see cref="TwitchApiService"/> class.
    /// </summary>
    /// <param name="settingsService">The service for accessing application settings (like API credentials).</param>
    /// <param name="tokenStorage">The service for securely storing and retrieving user tokens.</param>
    /// <param name="logger">The logger instance for logging messages.</param>
    /// <exception cref="ArgumentNullException">Thrown if any constructor parameters are null.</exception>
    public TwitchApiService(ISettingsService settingsService, ITokenStorageService tokenStorage, ILogger<TwitchApiService> logger)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _tokenStorage = tokenStorage ?? throw new ArgumentNullException(nameof(tokenStorage));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogInformation("Initialized.");
    }

    /// <summary>
    /// Ensures the TwitchAPI client instance is initialized and configured with appropriate credentials.
    /// It prioritizes using a user-specific access token if a <paramref name="userAccountId"/> is provided and a token exists.
    /// Otherwise, it falls back to using a cached or newly requested application access token.
    /// </summary>
    /// <param name="userAccountId">The Twitch User ID of the specific user context required for the API call, if any.
    /// If provided, the service will attempt to use this user's stored access token.</param>
    /// <returns>A configured <see cref="TwitchAPI"/> instance, or null if initialization fails (e.g., missing credentials or unable to get tokens).</returns>
    private async Task<TwitchAPI?> EnsureApiInitializedAsync(string? userAccountId = null)
    {
        string? clientId = _settingsService.CurrentSettings.Credentials.TwitchApiClientId;
        string? clientSecret = _settingsService.CurrentSettings.Credentials.TwitchApiClientSecret;

        if (
            string.IsNullOrWhiteSpace(clientId)
            || clientId.StartsWith("YOUR_")
            || string.IsNullOrWhiteSpace(clientSecret)
            || clientSecret.StartsWith("YOUR_")
        )
        {
            _logger.LogError("Cannot initialize Twitch API: Client ID or Secret is missing or appears invalid in settings.");
            return null;
        }

        _api ??= new TwitchAPI();
        _api.Settings.ClientId = clientId;
        _api.Settings.Secret = clientSecret;

        if (!string.IsNullOrEmpty(userAccountId))
        {
            string storageKey = $"twitch_{userAccountId}";
            _logger.LogDebug("Attempting to retrieve user token for Account ID: {UserAccountId} using key: {StorageKey}", userAccountId, storageKey);
            (string? userAccessToken, _) = await _tokenStorage.GetTokensAsync(storageKey);

            if (!string.IsNullOrEmpty(userAccessToken))
            {
                _api.Settings.AccessToken = userAccessToken;
                _logger.LogDebug("Using User Access Token for Account ID: {UserAccountId}.", userAccountId);
                return _api;
            }
            else
            {
                _logger.LogWarning(
                    "User token requested for Account ID {UserAccountId}, but none found in storage. Falling back to App Access Token.",
                    userAccountId
                );
            }
        }
        else
        {
            _logger.LogDebug("No specific User Account ID provided, attempting to use App Access Token.");
        }

        if (s_appAccessToken == null || s_appAccessTokenExpiry <= DateTimeOffset.UtcNow.AddMinutes(5))
        {
            _logger.LogInformation("App Access Token is missing, null, or expiring soon. Requesting new token...");
            try
            {
                string newAppAccessToken = await _api.Auth.GetAccessTokenAsync();
                if (!string.IsNullOrEmpty(newAppAccessToken))
                {
                    s_appAccessToken = newAppAccessToken;

                    s_appAccessTokenExpiry = DateTimeOffset.UtcNow.AddDays(50);
                    _api.Settings.AccessToken = s_appAccessToken;
                    _logger.LogInformation("Obtained and cached new App Access Token. Expires around: {ExpiryDate}", s_appAccessTokenExpiry);
                }
                else
                {
                    _logger.LogError("Failed to obtain a new App Access Token from Twitch API (response was null or empty).");
                    _api.Settings.AccessToken = null;
                    s_appAccessToken = null;
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while obtaining App Access Token: {ErrorMessage}", ex.Message);
                _api.Settings.AccessToken = null;
                s_appAccessToken = null;
                return null;
            }
        }
        else
        {
            _api.Settings.AccessToken = s_appAccessToken;
            _logger.LogDebug("Using cached App Access Token.");
        }

        return _api;
    }

    /// <summary>
    /// Fetches global Twitch chat badges using an App Access Token.
    /// </summary>
    /// <returns>The API response containing global badges, or null on error.</returns>
    public async Task<GetGlobalChatBadgesResponse?> GetGlobalBadgesAsync()
    {
        TwitchAPI? api = await EnsureApiInitializedAsync();
        if (api == null || string.IsNullOrEmpty(api.Settings.AccessToken))
        {
            _logger.LogWarning("Cannot get global badges: API client or App Access Token not available.");
            return null;
        }

        try
        {
            _logger.LogInformation("Fetching Global Twitch Chat Badges...");

            GetGlobalChatBadgesResponse? response = await api.Helix.Chat.GetGlobalChatBadgesAsync(api.Settings.AccessToken);
            _logger.LogDebug("Successfully fetched global badges. Set count: {SetCount}", response?.EmoteSet?.Length ?? 0);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching global Twitch badges: {ErrorMessage}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Fetches channel-specific Twitch chat badges for a given broadcaster.
    /// Requires context of an authenticated user (via User Access Token).
    /// </summary>
    /// <param name="broadcasterId">The Twitch User ID of the channel whose badges are requested.</param>
    /// <param name="userAccountId">The Twitch User ID of the authenticated user making the request (whose token will be used).</param>
    /// <returns>The API response containing channel badges, or null on error or if tokens are unavailable.</returns>
    public async Task<GetChannelChatBadgesResponse?> GetChannelBadgesAsync(string broadcasterId, string userAccountId)
    {
        TwitchAPI? api = await EnsureApiInitializedAsync(userAccountId);
        if (api == null || string.IsNullOrEmpty(api.Settings.AccessToken))
        {
            _logger.LogWarning(
                "Cannot get channel badges for Broadcaster ID {BroadcasterId}: API client or required User Access Token (for User ID {UserAccountId}) not available.",
                broadcasterId,
                userAccountId
            );
            return null;
        }

        try
        {
            _logger.LogInformation(
                "Fetching Channel Twitch Chat Badges for Broadcaster ID: {BroadcasterId} (using token for User ID: {UserAccountId})",
                broadcasterId,
                userAccountId
            );
            GetChannelChatBadgesResponse? response = await api.Helix.Chat.GetChannelChatBadgesAsync(broadcasterId, api.Settings.AccessToken);
            _logger.LogDebug(
                "Successfully fetched channel badges for {BroadcasterId}. Set count: {SetCount}",
                broadcasterId,
                response?.EmoteSet?.Length ?? 0
            );
            return response;
        }
        catch (BadScopeException bse)
        {
            _logger.LogError(
                bse,
                "Authorization error (Bad Scope) fetching channel badges for {BroadcasterId}. User token may lack required permissions. Message: {ErrorMessage}",
                broadcasterId,
                bse.Message
            );
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error fetching channel Twitch badges for Broadcaster ID {BroadcasterId}: {ErrorMessage}",
                broadcasterId,
                ex.Message
            );
            return null;
        }
    }

    /// <summary>
    /// Fetches global Twitch emotes using an App Access Token.
    /// </summary>
    /// <returns>The API response containing global emotes, or null on error.</returns>
    public async Task<GetGlobalEmotesResponse?> GetGlobalEmotesAsync()
    {
        TwitchAPI? api = await EnsureApiInitializedAsync();
        if (api == null || string.IsNullOrEmpty(api.Settings.AccessToken))
        {
            _logger.LogWarning("Cannot get global emotes: API client or App Access Token not available.");
            return null;
        }

        try
        {
            _logger.LogInformation("Fetching Global Twitch Emotes...");
            GetGlobalEmotesResponse? response = await api.Helix.Chat.GetGlobalEmotesAsync(api.Settings.AccessToken);
            _logger.LogDebug("Successfully fetched global emotes. Count: {EmoteCount}", response?.GlobalEmotes?.Length ?? 0);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching global Twitch emotes: {ErrorMessage}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Fetches channel-specific Twitch emotes (e.g., subscriber emotes) for a given broadcaster.
    /// Requires context of an authenticated user (via User Access Token).
    /// </summary>
    /// <param name="broadcasterId">The Twitch User ID of the channel whose emotes are requested.</param>
    /// <param name="userAccountId">The Twitch User ID of the authenticated user making the request (whose token will be used).</param>
    /// <returns>The API response containing channel emotes, or null on error or if tokens are unavailable.</returns>
    public async Task<GetChannelEmotesResponse?> GetChannelEmotesAsync(string broadcasterId, string userAccountId)
    {
        TwitchAPI? api = await EnsureApiInitializedAsync(userAccountId);
        if (api == null || string.IsNullOrEmpty(api.Settings.AccessToken))
        {
            _logger.LogWarning(
                "Cannot get channel emotes for Broadcaster ID {BroadcasterId}: API client or required User Access Token (for User ID {UserAccountId}) not available.",
                broadcasterId,
                userAccountId
            );
            return null;
        }

        try
        {
            _logger.LogInformation(
                "Fetching Channel Twitch Emotes for Broadcaster ID: {BroadcasterId} (using token for User ID: {UserAccountId})",
                broadcasterId,
                userAccountId
            );
            GetChannelEmotesResponse? response = await api.Helix.Chat.GetChannelEmotesAsync(broadcasterId, api.Settings.AccessToken);
            _logger.LogDebug(
                "Successfully fetched channel emotes for {BroadcasterId}. Count: {EmoteCount}",
                broadcasterId,
                response?.ChannelEmotes?.Length ?? 0
            );
            return response;
        }
        catch (BadScopeException bse)
        {
            _logger.LogError(
                bse,
                "Authorization error (Bad Scope) fetching channel emotes for {BroadcasterId}. User token may lack required permissions. Message: {ErrorMessage}",
                broadcasterId,
                bse.Message
            );
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error fetching channel Twitch emotes for Broadcaster ID {BroadcasterId}: {ErrorMessage}",
                broadcasterId,
                ex.Message
            );
            return null;
        }
    }

    /// <summary>
    /// Retrieves user information from the Twitch API based on a login name.
    /// </summary>
    /// <param name="loginName">The Twitch login name (username) to look up.</param>
    /// <param name="requestingUserAccountId">Optional: The User ID of the authenticated user making the request, to potentially use their user token.</param>
    /// <returns>A <see cref="User"/> object if found, otherwise null.</returns>
    public async Task<User?> GetUserInfoAsync(string loginName, string? requestingUserAccountId = null)
    {
        if (string.IsNullOrWhiteSpace(loginName))
        {
            _logger.LogWarning("GetUserInfoAsync called with null or empty login name.");
            return null;
        }

        TwitchAPI? api = await EnsureApiInitializedAsync(requestingUserAccountId);
        if (api == null || string.IsNullOrEmpty(api.Settings.AccessToken))
        {
            _logger.LogWarning("Cannot get user info for Login Name '{LoginName}': API client or required Access Token not available.", loginName);
            return null;
        }

        try
        {
            _logger.LogDebug("Requesting user info from Twitch API for Login Name: {LoginName}", loginName);

            GetUsersResponse? usersResponse = await api.Helix.Users.GetUsersAsync(
                logins: [loginName.ToLowerInvariant()],
                accessToken: api.Settings.AccessToken
            );
            User? user = usersResponse?.Users.FirstOrDefault();

            if (user != null)
            {
                _logger.LogDebug("Successfully retrieved user info for Login Name {LoginName}. User ID: {UserId}", loginName, user.Id);
            }
            else
            {
                _logger.LogWarning("User info not found for Login Name {LoginName}.", loginName);
            }

            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Twitch user info for Login Name {LoginName}: {ErrorMessage}", loginName, ex.Message);
            return null;
        }
    }
}
