using StreamWeaver.Core.Models.Settings;

namespace StreamWeaver.Core.Services.Platforms;

public interface ITwitchClient
{
    /// <summary>
    /// Initiates a connection for a specific Twitch account.
    /// </summary>
    /// <param name="accountId">The Twitch User ID of the account to connect.</param>
    /// <param name="username">The Twitch username for the connection.</param>
    /// <param name="accessToken">The OAuth access token for the account.</param>
    /// <returns>True if the connection attempt was initiated, false otherwise.</returns>
    Task<bool> ConnectAsync(string accountId, string username, string accessToken);

    /// <summary>
    /// Disconnects a specific Twitch account's connection.
    /// </summary>
    /// <param name="accountId">The Twitch User ID of the account to disconnect.</param>
    Task DisconnectAsync(string accountId);

    /// <summary>
    /// Joins a chat channel using a specific account's connection.
    /// </summary>
    /// <param name="accountId">The Twitch User ID of the account to use.</param>
    /// <param name="channelName">The name of the channel to join.</param>
    Task JoinChannelAsync(string accountId, string channelName);

    /// <summary>
    /// Leaves a chat channel using a specific account's connection.
    /// </summary>
    /// <param name="accountId">The Twitch User ID of the account to use.</param>
    /// <param name="channelName">The name of the channel to leave.</param>
    Task LeaveChannelAsync(string accountId, string channelName);

    /// <summary>
    /// Sends a chat message to a channel using a specific account's connection.
    /// </summary>
    /// <param name="accountId">The Twitch User ID of the account sending the message.</param>
    /// <param name="channelName">The target channel name.</param>
    /// <param name="message">The message content.</param>
    Task SendMessageAsync(string accountId, string channelName, string message);

    /// <summary>
    /// Gets the current connection status for a specific account.
    /// </summary>
    /// <param name="accountId">The Twitch User ID of the account.</param>
    /// <returns>The connection status.</returns>
    ConnectionStatus GetStatus(string accountId);

    /// <summary>
    /// Gets the current status message (e.g., error details) for a specific account.
    /// </summary>
    /// <param name="accountId">The Twitch User ID of the account.</param>
    /// <returns>The status message, or null if none.</returns>
    string? GetStatusMessage(string accountId);
}
