using CommunityToolkit.Mvvm.Messaging;

namespace StreamWeaver.Core.Plugins;

/// <summary>
/// Provides methods for plugins to interact with the StreamWeaver core application.
/// </summary>
public interface IPluginHost
{
    /// <summary>
    /// Gets the application's main messenger instance for subscribing to or publishing events.
    /// </summary>
    /// <returns>The active IMessenger instance.</returns>
    IMessenger GetMessenger();

    /// <summary>
    /// Sends a chat message through the StreamWeaver core to the specified platform and target.
    /// Handles selecting the correct authenticated account and sending mechanism.
    /// </summary>
    /// <param name="platform">The target platform (e.g., "Twitch", "YouTube").</param>
    /// <param name="senderAccountId">The specific authenticated account ID to send the message from.</param>
    /// <param name="target">The destination channel name (Twitch) or Live Chat ID (YouTube).</param>
    /// <param name="message">The message content to send.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SendChatMessageAsync(string platform, string senderAccountId, string target, string message);

    // Future considerations:
    // - Access to read-only settings? GetSettingValue<T>(string key)?
    // - Access to specific core services? GetService<T>()? (Use with caution)
    // - Method to log messages through StreamWeaver's logging system? Log(level, message)?
}
