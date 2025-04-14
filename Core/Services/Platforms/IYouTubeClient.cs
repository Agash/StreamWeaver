using StreamWeaver.Core.Models.Settings;

namespace StreamWeaver.Core.Services.Platforms;

/// <summary>
/// Interface for a service managing YouTube Live Chat interactions for potentially multiple accounts.
/// </summary>
public interface IYouTubeClient
{
    /// <summary>
    /// Initializes the YouTube API service for a specific account using an access token.
    /// </summary>
    /// <param name="accountId">The YouTube Channel ID of the account to connect.</param>
    /// <param name="accessToken">The OAuth 2.0 access token.</param>
    /// <returns>True if initialization was successful (Connected or Limited state), false otherwise.</returns>
    Task<bool> ConnectAsync(string accountId, string accessToken);

    /// <summary>
    /// Disconnects the API service and stops polling for a specific account.
    /// </summary>
    /// <param name="accountId">The YouTube Channel ID of the account to disconnect.</param>
    Task DisconnectAsync(string accountId);

    /// <summary>
    /// Finds the active broadcast Video ID for the specified account's current live stream using the official API.
    /// Used primarily to determine the default Video ID if no override is specified.
    /// </summary>
    /// <param name="accountId">The YouTube Channel ID of the account.</param>
    /// <returns>The active broadcast's Video ID if an active stream is found, otherwise null.</returns>
    Task<string?> FindActiveVideoIdAsync(string accountId);

    /// <summary>
    /// Starts polling for new chat messages for a specific account and Video ID using YTLiveChat.
    /// This may internally attempt to fetch the associated LiveChatId for later API actions.
    /// </summary>
    /// <param name="accountId">The YouTube Channel ID of the account.</param>
    /// <param name="videoId">The Video ID to poll.</param>
    Task StartPollingAsync(string accountId, string videoId);

    /// <summary>
    /// Stops polling for chat messages for a specific account.
    /// </summary>
    /// <param name="accountId">The YouTube Channel ID of the account to stop polling for.</param>
    Task StopPollingAsync(string accountId);

    /// <summary>
    /// Sends a chat message to a specified live chat using a specific account via the official API.
    /// </summary>
    /// <param name="accountId">The YouTube Channel ID of the account sending the message.</param>
    /// <param name="liveChatId">The target Live Chat ID (NOT the Video ID).</param>
    /// <param name="message">The message content.</param>
    Task SendMessageAsync(string accountId, string liveChatId, string message);

    /// <summary>
    /// Gets the current connection status for a specific YouTube account.
    /// </summary>
    /// <param name="accountId">The YouTube Channel ID.</param>
    /// <returns>The connection status.</returns>
    ConnectionStatus GetStatus(string accountId);

    /// <summary>
    /// Gets the current status message for a specific YouTube account.
    /// </summary>
    /// <param name="accountId">The YouTube Channel ID.</param>
    /// <returns>The status message, or null.</returns>
    string? GetStatusMessage(string accountId);

    /// <summary>
    /// Gets the currently active Video ID being polled by YTLiveChat for a specific account, if any.
    /// </summary>
    /// <param name="accountId">The YouTube Channel ID.</param>
    /// <returns>The active Video ID or null.</returns>
    string? GetActiveVideoId(string accountId);

    /// <summary>
    /// Gets the actual Live Chat ID associated with the active stream being monitored by a specific account, retrieved via the official API.
    /// Required for sending messages and performing moderation actions. Returns the cached value if available.
    /// </summary>
    /// <param name="accountId">The YouTube Channel ID.</param>
    /// <returns>The cached Live Chat ID if available, otherwise null.</returns>
    string? GetAssociatedLiveChatId(string accountId);

    /// <summary>
    /// Looks up the Live Chat ID for the given Video ID using the official API, stores it internally, and returns it.
    /// This is useful if the Live Chat ID wasn't retrieved during the initial connection (e.g., due to overrides or quota).
    /// </summary>
    /// <param name="accountId">The YouTube Channel ID performing the lookup.</param>
    /// <param name="videoId">The Video ID for which to find the Live Chat ID.</param>
    /// <returns>The Live Chat ID if found, otherwise null.</returns>
    Task<string?> LookupAndStoreLiveChatIdAsync(string accountId, string videoId);

    // --- Moderation/Interaction Method Signatures ---
    Task DeleteMessageAsync(string moderatorAccountId, string messageId);
    Task TimeoutUserAsync(string moderatorAccountId, string liveChatId, string userIdToTimeout, uint durationSeconds);
    Task BanUserAsync(string moderatorAccountId, string liveChatId, string userIdToBan);

    // PinMessageAsync removed as it's not supported by the current library/API version.
    // Task PinMessageAsync(string moderatorAccountId, string liveChatId, string messageId);

    // --- Poll Methods ---
    /// <summary>
    /// Creates a new poll in the specified live chat.
    /// </summary>
    /// <param name="moderatorAccountId">The Channel ID of the account creating the poll.</param>
    /// <param name="liveChatId">The ID of the live chat to create the poll in.</param>
    /// <param name="question">The poll question (max 100 chars).</param>
    /// <param name="options">A list of poll options (2-5 options, max 30 chars each).</param>
    /// <returns>The ID of the created poll LiveChatMessage, or null if creation failed.</returns>
    Task<string?> CreatePollAsync(string moderatorAccountId, string liveChatId, string question, List<string> options);

    /// <summary>
    /// Transitions the status of an existing poll message (e.g., ends it).
    /// </summary>
    /// <param name="moderatorAccountId">The Channel ID of the account ending the poll.</param>
    /// <param name="pollMessageId">The ID of the LiveChatMessage representing the poll to transition.</param>
    /// <param name="status">The target status (e.g., "ended").</param>
    /// <returns>True if the transition was successful, false otherwise.</returns>
    Task<bool> EndPollAsync(string moderatorAccountId, string pollMessageId, string status = "ended");
}
